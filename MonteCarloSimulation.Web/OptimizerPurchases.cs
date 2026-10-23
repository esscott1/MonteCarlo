using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web
{
    // The annuity purchases the Optimizer weighs, for both its endpoints (/api/optimal, and /api/run/optimize on the
    // Scenario runner's inputs): a SPIA at today's rate, with Tax Deferred money or with after-tax money (Brokerage, then
    // Roth; a non-qualified annuity). For a visitor with the annuity feature unless they chose not to (considerAnnuities),
    // and for the Free teaser (a man's rate) while a paid tier offers annuities, so it can say how much one might add.
    // Each size is weighed with the app's COLA too, the same way: with the annuity-cola feature, or for the teaser while a
    // paid tier offers it. A woman's rate needs annuity-gender. Null when none is weighed, or the index has no rate.
    public static class OptimizerPurchases
    {
        public static async Task<AnnuityPurchaseOptions?> ForAsync(
            Access access, bool considerAnnuities, double ageAtRetirement, bool female, SpiaPayoutIndex spiaIndex, CancellationToken cancellationToken)
        {
            bool teaser = !access.Can(Features.OptimizerFull);
            bool Weighs(string feature) => access.Can(feature) || (teaser && access.Offers.Any(offer => offer.Features.Contains(feature)));
            bool weighPurchase = access.Can(Features.Annuity) ? considerAnnuities : Weighs(Features.Annuity);
            if (!weighPurchase) return null;
            return OptimalRequest.PurchaseOptionsAt(await spiaIndex.CurrentAsync(cancellationToken), ageAtRetirement,
                female: female && access.Can(Features.AnnuityGender),
                cola: Weighs(Features.AnnuityCola),
                afterTax: true);
        }
    }
}
