# Notices

The code in this repository is under the PolyForm Noncommercial License 1.0.0 ([LICENSE.md](LICENSE.md)), except for the
third-party material below, which keeps its own license.

## LifeAnnuities.us SPIA Payout Index

- **File:** [MonteCarloSimulation.Web/SpiaPayoutIndex.seed.json](MonteCarloSimulation.Web/SpiaPayoutIndex.seed.json)
- **What it is:** a copy of the LifeAnnuities.us SPIA Payout Index data file
  (<https://lifeannuities.us/rates/spia-payout-index.json>), downloaded October 15, 2026 with figures as of
  October 1, 2026. It is unmodified apart from line endings.
- **Source:** LifeAnnuities.us SPIA Payout Index, <https://lifeannuities.us/rates/spia-payout-index/>
- **License:** Creative Commons Attribution 4.0 International (CC BY 4.0),
  <https://creativecommons.org/licenses/by/4.0/>. It is not covered by LICENSE.md.
- **Endorsement:** LifeAnnuities.us does not endorse this project.

The web app uses this file until it fetches the current index, and then the fetched figures, to suggest a SPIA payout
on the Scenario runner's Annuity tab. Ages the index doesn't list are estimated from its figures with an annuity-pricing
formula, and the page labels those estimates as estimated. The page credits the index and links to it and to the
license wherever the figures appear.
