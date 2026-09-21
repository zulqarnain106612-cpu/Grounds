using System;
using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Economy
{
    /// <summary>
    /// The real-money products, as data.
    ///
    /// The cell's criterion is that this loads and validates -- no duplicate
    /// or malformed ids -- *before any store UI depends on it*. That ordering
    /// is the point: a duplicate product id surfaces at runtime as a purchase
    /// crediting the wrong amount, which is a refund conversation rather than
    /// a bug report, and it is discovered by a customer.
    /// </summary>
    [CreateAssetMenu(menuName = "JetFighter/Product Catalog", fileName = "ProductCatalog")]
    public class ProductCatalog : ScriptableObject
    {
        [Serializable]
        public struct Product
        {
            [Tooltip("Must match the App Store Connect product id exactly. Case-sensitive.")]
            public string productId;

            public string displayName;

            [Tooltip("Gems credited on purchase, before the tier bonus.")]
            [Min(1)]
            public int baseGems;

            [Tooltip("Extra percent granted at this tier. 20 means +20%.")]
            [Range(0f, 200f)]
            public float bonusPercent;

            [Tooltip("Ordering hint for the store. Not a price -- prices come from the store.")]
            [Min(0)]
            public int tier;
        }

        [Tooltip("Products, in tier order. Ids must be unique and non-empty.")]
        public List<Product> products = new List<Product>();

        /// <summary>
        /// Gems a product grants, base plus tier bonus, rounded down.
        ///
        /// Down rather than nearest: a player who computes the advertised
        /// bonus and gets one gem fewer files a complaint, and a player who
        /// gets one more never does. The advertised number should be the
        /// floor.
        /// </summary>
        public static int GemsFor(Product product)
        {
            float bonus = Mathf.Max(0f, product.bonusPercent) / 100f;
            return Mathf.FloorToInt(Mathf.Max(0, product.baseGems) * (1f + bonus));
        }

        /// <summary>Finds a product by id, or returns false. Never throws on unknown ids.</summary>
        public bool TryGet(string productId, out Product product)
        {
            product = default;
            if (string.IsNullOrWhiteSpace(productId))
            {
                return false;
            }
            foreach (Product candidate in products)
            {
                // Ordinal: an App Store product id is an exact byte match, and
                // culture-aware comparison would accept ids the store will not.
                if (string.Equals(candidate.productId, productId, StringComparison.Ordinal))
                {
                    product = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Every problem with this catalog, as readable messages.
        ///
        /// Returns a list rather than throwing on the first fault: a designer
        /// fixing a catalog wants all of them, not one per build.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < products.Count; i++)
            {
                Product product = products[i];
                string where = $"products[{i}]";

                if (string.IsNullOrWhiteSpace(product.productId))
                {
                    problems.Add($"{where}: product id is empty");
                }
                else if (!seen.Add(product.productId))
                {
                    // The fault that becomes a refund conversation.
                    problems.Add($"{where}: duplicate product id '{product.productId}'");
                }
                else if (product.productId.Trim() != product.productId)
                {
                    // A trailing space is invisible in the inspector and makes
                    // the id not match App Store Connect, so every purchase
                    // fails with no obvious cause.
                    problems.Add($"{where}: product id '{product.productId}' has leading or trailing whitespace");
                }

                if (product.baseGems <= 0)
                {
                    problems.Add($"{where}: grants {product.baseGems} gems, so a purchase would give nothing");
                }
                if (product.bonusPercent < 0f)
                {
                    problems.Add($"{where}: negative bonus");
                }
                if (GemsFor(product) < product.baseGems)
                {
                    problems.Add($"{where}: bonus reduces the grant below its base");
                }
            }

            // A higher tier granting fewer gems is a pricing mistake that
            // makes the expensive option strictly worse, and no store UI can
            // present it in a way that is not a bug.
            var byTier = new List<Product>(products);
            byTier.Sort((a, b) => a.tier.CompareTo(b.tier));
            for (int i = 1; i < byTier.Count; i++)
            {
                if (byTier[i].tier != byTier[i - 1].tier && GemsFor(byTier[i]) < GemsFor(byTier[i - 1]))
                {
                    problems.Add(
                        $"tier {byTier[i].tier} ('{byTier[i].productId}') grants fewer gems than " +
                        $"tier {byTier[i - 1].tier} ('{byTier[i - 1].productId}')");
                }
            }

            return problems;
        }

        /// <summary>True when the catalog has no problems and at least one product.</summary>
        public bool IsUsable => products.Count > 0 && Validate().Count == 0;
    }
}
