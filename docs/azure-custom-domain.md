# Binding montecarlo.otsconsulting.ai to Azure App Service

The web app is deployed via `.github/workflows/deploy-azure.yml` on every push to `master`, and is already live at `https://montecarlo-otsconsulting.azurewebsites.net/`. The steps below are the remaining one-time manual work to make it reachable at `https://montecarlo.otsconsulting.ai` instead.

Resources already provisioned:

- Resource group: `rg-montecarlo`
- Region: `westus2`
- Web App name: `montecarlo-otsconsulting`

## 1. Add DNS records in GoDaddy

In GoDaddy DNS management for `otsconsulting.ai`, add:

- `CNAME` record: `montecarlo` → `montecarlo-otsconsulting.azurewebsites.net`
- `TXT` record: `asuid.montecarlo` → the Custom Domain Verification ID, obtained via:
  ```bash
  az webapp show -g rg-montecarlo -n montecarlo-otsconsulting \
    --query customDomainVerificationId -o tsv
  ```

DNS changes can take anywhere from a few minutes to a few hours to propagate.

## 2. Bind the hostname

Once DNS has propagated (verify with `nslookup montecarlo.otsconsulting.ai`):

```bash
az webapp config hostname add -g rg-montecarlo -n montecarlo-otsconsulting \
  --hostname montecarlo.otsconsulting.ai
```

## 3. Create and bind a free managed SSL certificate

```bash
az webapp config ssl create -g rg-montecarlo -n montecarlo-otsconsulting \
  --hostname montecarlo.otsconsulting.ai
```

Note the certificate thumbprint returned, then bind it:

```bash
az webapp config ssl bind -g rg-montecarlo -n montecarlo-otsconsulting \
  --certificate-thumbprint <thumbprint> --ssl-type SNI
```

## 4. Enforce HTTPS (optional but recommended)

```bash
az webapp update -g rg-montecarlo -n montecarlo-otsconsulting --https-only true
```

## Verify

Browse to `https://montecarlo.otsconsulting.ai` and confirm:

- The page loads over HTTPS with a valid certificate (no browser warning)
- `GET /api/scenarios` populates the scenario dropdown
- Running a scenario via `POST /api/run` returns results

If anything fails, tail live logs with:

```bash
az webapp log tail -g rg-montecarlo -n montecarlo-otsconsulting
```
