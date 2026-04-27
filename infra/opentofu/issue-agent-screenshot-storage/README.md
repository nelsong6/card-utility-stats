# Issue-Agent Screenshot Storage

This OpenTofu stack provisions SpireLens-owned public-read Azure Blob Storage for issue-agent screenshot previews.

It creates:

- a resource group
- a StorageV2 account with public blob access enabled
- a public-read container for screenshot PNGs
- a user-assigned managed identity for GitHub Actions uploads
- GitHub OIDC federated identity credentials for the configured subjects
- `Storage Blob Data Contributor` on the storage account for the upload identity
- an optional lifecycle rule to delete old screenshots

## Pipeline

This stack is applied from GitHub Actions through the shared reusable workflow in `nelsong6/pipeline-templates`:

```yaml
uses: nelsong6/pipeline-templates/.github/workflows/tofu-plan-apply-template.yml@main
```

State uses the same backend convention as `nelsong6/infra-bootstrap`:

- resource group: `infra`
- storage account: `${{ vars.TFSTATE_STORAGE_ACCOUNT }}`
- container: `tfstate`
- key: `spirelens/issue-agent-screenshot-storage.tfstate`

Do not apply this stack locally. Dispatch or push through `.github/workflows/opentofu-issue-agent-screenshot-storage.yml`.

## Authentication Model

GitHub Actions should use OIDC through `azure/login` with the `github_uploader_client_id` output.

Example upload shape:

```yaml
permissions:
  id-token: write
  contents: read

steps:
  - uses: azure/login@v2
    with:
      client-id: ${{ vars.AZURE_SCREENSHOT_UPLOADER_CLIENT_ID }}
      tenant-id: ${{ vars.ARM_TENANT_ID }}
      subscription-id: ${{ vars.ARM_SUBSCRIPTION_ID }}

  - name: Upload screenshots
    shell: powershell
    run: |
      az storage blob upload-batch `
        --auth-mode login `
        --account-name $env:AZURE_SCREENSHOT_STORAGE_ACCOUNT `
        --destination $env:AZURE_SCREENSHOT_CONTAINER `
        --destination-path $env:GITHUB_RUN_ID `
        --source $env:SCREENSHOT_DIR `
        --pattern *.png `
        --overwrite true
```

The public preview URL for a screenshot is:

```text
https://<storage-account>.blob.core.windows.net/<container>/<run-id>/<filename>.png
```

## Outputs To Consume

After apply, the upload workflow needs these stack outputs:

- `github_uploader_client_id`
- `storage_account_name`
- `container_name`
- `container_url`

Those can be read from state with `nelsong6/pipeline-templates/.github/workflows/tofu-outputs-template.yml@main`.
