namespace MonteCarloSimulation.Web
{
    // The Annuity tab's suggested rate: GET /api/spia-rate?age=61.4&sex=male. The best SPIA payout per $100,000 a month
    // for that age (nearest birthday) from the LifeAnnuities.us SPIA Payout Index, estimated by SpiaPayoutModel where the
    // index has no figure, with the figures' as-of date and how to credit them. The annuity is a Plus feature; choosing
    // the annuitant's sex is Pro's (Plus always gets the male rate). Whether the index could be fetched never shows.
    public static class SpiaRateEndpoints
    {
        public static void MapSpiaRate(this WebApplication app)
        {
            app.MapGet("/api/spia-rate", async (
                double? age, string? sex, HttpContext context, FeatureAccess features, SpiaPayoutIndex index) =>
            {
                var errors = Validate(age, sex, out var annuitant);
                if (errors.Count > 0)
                    return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));

                var access = await features.ForAsync(context);
                var locked = FreeTier.LockedSpiaRate(annuitant, access);
                if (locked.Count > 0) return FreeTier.Refused(locked);

                var figures = await index.CurrentAsync(context.RequestAborted);
                var rate = figures.Model.MonthlyPer100k(age!.Value, annuitant);
                return Results.Ok(new SpiaRateResponse(
                    rate?.Monthly,
                    (int)Math.Round(age.Value, MidpointRounding.AwayFromZero),
                    annuitant == AnnuitantSex.Female ? "female" : "male",
                    rate?.Estimated ?? true,
                    rate is null ? "age" : null,
                    figures.AsOf,
                    figures.Basis,
                    figures.Attribution,
                    figures.SourceUrl,
                    figures.LicenseUrl));
            });
        }

        public static Dictionary<string, string> Validate(double? age, string? sex, out AnnuitantSex annuitant)
        {
            var errors = new Dictionary<string, string>();
            if (age is not double years || !double.IsFinite(years) || years < 0 || years > 120)
                errors["age"] = "Age must be between 0 and 120.";
            annuitant = AnnuitantSex.Male;
            switch (sex?.Trim().ToLowerInvariant())
            {
                case null or "" or "male": break;
                case "female": annuitant = AnnuitantSex.Female; break;
                default: errors["sex"] = "Gender must be male or female."; break;
            }
            return errors;
        }
    }

    // MonthlyPer100k is null (Reason "age") outside the ages the rate covers, 55 to 80
    public sealed record SpiaRateResponse(
        double? MonthlyPer100k, int Age, string Sex, bool Estimated, string? Reason, string AsOf, string Basis,
        string Attribution, string SourceUrl, string LicenseUrl);
}
