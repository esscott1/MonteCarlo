# Infrastructure

Terraform for the Azure pieces the app needs beyond the Web App. Today that is one root module,
`foundry/`: the Microsoft Foundry account that serves Claude to **Gordon**, the chat panel
([docs/plan-gordon-chat-bot.md](../docs/plan-gordon-chat-bot.md), Part 1). When phase 3 of the paid
tiers plan adds a dev environment, this becomes `modules/` + `envs/`.

## What `foundry/` owns, and what it doesn't

| Owns | Doesn't own |
|---|---|
| Foundry account `aifoundry-montecarlo` (eastus2, kind `AIServices`, SKU `S0`, keys disabled) | The resource group `rg-montecarlo` |
| Foundry project `montecarlo` | The App Service plan and the Web App `montecarlo-otsconsulting` |
| Deployment `claude-haiku-4-5` (`GlobalStandard`, 25k tokens a minute) | The Web App's app settings (`next_step` prints the command) |
| Cognitive Services User on the account for the Web App's managed identity | The custom domain and certificate |
| The same role for the operator (`operator_principal_id`), for local runs | The state storage account |

The Web App and resource group are read through data sources, never managed: `terraform destroy`
here removes the Foundry account and nothing else.

**Applying accepts the Anthropic Marketplace offer terms.** Every Claude deployment on Foundry carries
`modelProviderData` (organization, country, industry), and creating it subscribes to the offer. Read
the [commercial terms](https://www.anthropic.com/legal/commercial-terms) and check those three values in
`terraform.tfvars` before the first apply.

## Cost

No standing charge: S0 has no hourly fee, projects are free, and `GlobalStandard` capacity is a rate
cap, not a reservation. Only the tokens used are billed.

## Bootstrap (once, by hand)

Already done for this subscription. These are what Terraform can't create for itself:

1. **Terraform 1.16.x** (`windows_amd64`) and **Azure CLI**, `az login` into the `az-303 learn`
   subscription (`a6ff75f7-…`), Owner or Contributor on `rg-montecarlo`, and
   `Microsoft.CognitiveServices` registered (`azapi` doesn't auto-register providers).
2. **State storage**, Entra auth only:
   ```powershell
   az group create -n rg-terraform-state -l westus2
   az storage account create -n esmontecarloterrastate -g rg-terraform-state -l westus2 --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false --allow-shared-key-access false
   $me = az ad signed-in-user show --query id -o tsv; $sa = az storage account show -n esmontecarloterrastate -g rg-terraform-state --query id -o tsv; az role assignment create --assignee $me --role "Storage Blob Data Contributor" --scope $sa
   az storage container create -n tfstate --account-name esmontecarloterrastate --auth-mode login
   az storage account blob-service-properties update -n esmontecarloterrastate -g rg-terraform-state --enable-versioning true --enable-delete-retention true --delete-retention-days 30
   ```
   Owner is control-plane only: without **Storage Blob Data Contributor** the backend gets 403.
3. **The Web App's system-assigned identity**, which `main.tf` reads:
   `az webapp identity assign -g rg-montecarlo -n montecarlo-otsconsulting`
4. **Claude quota.** Check `az cognitiveservices usage list -l eastus2 -o table` for
   `AIServices.GlobalStandard.claude-haiku-4-5`. A limit of 0 means the deployment will fail with a
   quota error until quota is granted (Foundry portal → Quota).

## Running it

```powershell
cd infra/foundry
copy terraform.tfvars.example terraform.tfvars   # first time; fill it in
terraform init
terraform plan -out tfplan
terraform apply tfplan
```

Then run the command `terraform output -raw next_step` prints, which sets `Foundry__Endpoint` and
`Foundry__Deployment` on the Web App. With managed identity there is no key to store.

Check that the model answers (401: wrong token scope; 403: the role hasn't reached the data plane yet,
wait a few minutes; 404: wrong URL or deployment name):

```bash
curl -X POST "https://aifoundry-montecarlo.services.ai.azure.com/anthropic/v1/messages" \
  -H "Authorization: Bearer $(az account get-access-token --scope https://ai.azure.com/.default --query accessToken -o tsv)" \
  -H "anthropic-version: 2023-06-01" -H "Content-Type: application/json" \
  -d '{"model":"claude-haiku-4-5","max_tokens":64,"messages":[{"role":"user","content":"ping"}]}'
```

A second `terraform apply` must report **No changes**; anything else is drift.

## Gotchas

- **Model version.** Foundry's versions aren't the API's dates: `claude-haiku-4-5` is `2` (default)
  or `20251001`. List them with
  `az cognitiveservices model list -l eastus2 --query "[?model.format=='Anthropic'].{name:model.name,version:model.version}" -o table`.
- **Norton (or any TLS-intercepting antivirus).** The Azure CLI is Python and doesn't use the Windows
  certificate store, so it fails with `CERTIFICATE_VERIFY_FAILED`, and Terraform's backend and
  providers fail with it, since they get their token from `az`. Point `REQUESTS_CA_BUNDLE` at a copy of
  the CLI's `certifi\cacert.pem` with the antivirus's root certificate appended (Norton's is
  `C:\ProgramData\Norton\Antivirus\wscert.pem`).
- **Long paths.** Windows won't start a process whose working directory is over 260 characters, and
  Terraform reports it as `fork/exec …az.cmd: The parameter is incorrect`. Run it from a short path.
- **The lock file** (`.terraform.lock.hcl`) is committed with Windows and Linux hashes, so CI can use
  it later: `terraform providers lock -platform=windows_amd64 -platform=linux_amd64` after a provider
  change.
