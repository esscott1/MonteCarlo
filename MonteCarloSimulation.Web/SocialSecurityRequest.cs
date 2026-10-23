using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web
{
    // The Scenario runner's Optimize Social Security (POST /api/run/optimize-social-security): the runner's own inputs,
    // its spend included, plus the Social Security estimates at 62, 67 and 70 from its flyout (as on the Optimal page)
    public class SocialSecurityRequest : RunRequest
    {
        public double SocialSecurityAt62 { get; set; }
        public double SocialSecurityAt67 { get; set; }
        public double SocialSecurityAt70 { get; set; }

        // The three estimates, checked as the Optimal page's are (its messages, so the page translates them the same way)
        public Dictionary<string, string> ValidateEstimates()
        {
            var errors = new Dictionary<string, string>();
            if (SocialSecurityAt62 < 0 || SocialSecurityAt67 < 0 || SocialSecurityAt70 < 0)
                errors["socialSecurity"] = "Social Security amounts must be non-negative.";
            else if (SocialSecurityAt62 > SocialSecurityAt67 || SocialSecurityAt67 > SocialSecurityAt70)
                errors["socialSecurity"] = "Social Security amounts should increase with age: 62 <= 67 <= 70.";
            return errors;
        }

        public SocialSecurityCurve Curve => new(SocialSecurityAt62, SocialSecurityAt67, SocialSecurityAt70);

        // The Social Security start that lets the most of the Optimizer's 167 markets survive at the runner's own spend
        public SocialSecurityChoice BestClaim(CancellationToken cancellationToken) =>
            SocialSecurityOptimizer.BestClaim(ToParameters(), Curve, Withdrawal, SpendingOptimizer.PathChoices[^1], cancellationToken);
    }
}
