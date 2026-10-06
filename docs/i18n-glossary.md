# Spanish glossary

The Spanish on the landing page, the Scenario runner and Model Info uses these terms, so the same idea always reads the same way.
Correct a term here and in `MonteCarloSimulation.Web/wwwroot/i18n/es.json` together. The full side-by-side list of every
string is [i18n-review.csv](i18n-review.csv) (regenerate it with `node tools/i18n/review-sheet.mjs`).

Register: formal (*usted*), US Spanish. Numbers and money keep the US format (`$1,234.56`), which US Spanish also uses
and which the money inputs expect.

| English | Spanish | Notes |
|---|---|---|
| Social Security | Seguro Social | "SS" in tight table headings stays "SS" |
| Tax Deferred (accounts, withdrawal) | con impuestos diferidos | |
| Brokerage (accounts) | (cuentas de) corretaje | "selling Brokerage" → "vendiendo inversiones de corretaje" |
| Roth, Roth conversion | Roth, conversión a Roth | the account name isn't translated |
| basis | base de costo | |
| unrealized gains | ganancias no realizadas | |
| capital gains / LTCG | ganancias de capital / LTCG | the abbreviation is kept, as on US tax forms |
| ordinary income tax | impuesto sobre el ingreso ordinario | |
| tax bracket | tramo (impositivo) | "top of the 22% bracket" → "tope del tramo del 22%" |
| standard deduction | deducción estándar | |
| Medicare IRMAA surcharge | recargo IRMAA de Medicare | names of US programs aren't translated |
| withdrawal | retiro | |
| survival (of the money) | supervivencia | "the money lasts" → "el dinero alcanza" |
| simulated markets / market paths | mercados simulados / trayectorias de mercado | |
| run (one simulation) | simulación | |
| Tax-optimized / Pro-rata (order) | optimizado para impuestos / proporcional | |
| Scenario runner | Simulador de escenarios | |
| Monte Carlo Portfolio Optimizer | Optimizador de cartera Monte Carlo | |
| change request | solicitud de cambio | |
| passphrase | frase de acceso | the Observe passphrase box stays in English |
| pull request | solicitud de incorporación de cambios (pull request) | |
| Model Info | Información del modelo | |
| household (lab) | hogar | |
| baseline | referencia | "the Tax-optimized baseline" → "la referencia optimizada para impuestos" |
| 82.5% spend | gasto al 82.5% | |
| 12% fill | llenado del 12% | |
| 0% gain harvesting | cosecha de ganancias al 0% | |
| 0% band (capital gains) | franja del 0% | |
| IRMAA tier | nivel de IRMAA | |
| market path | trayectoria de mercado | |
| gap / shortfall | diferencia | |
| Strategy Lab | Strategy Lab | the tool's name isn't translated |
