variable "name_prefix" {
  description = "Short lowercase prefix used to derive Azure resource names."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", var.name_prefix))
    error_message = "name_prefix must start and end with a lowercase letter or number and may contain hyphens in the middle."
  }
}

variable "location" {
  description = "Azure region for the VMSS deployment."
  type        = string
}

variable "resource_group_name" {
  description = "Optional override for the resource group name."
  type        = string
  default     = null
}

variable "vnet_name" {
  description = "Optional override for the virtual network name."
  type        = string
  default     = null
}

variable "subnet_name" {
  description = "Optional override for the subnet name."
  type        = string
  default     = null
}

variable "nsg_name" {
  description = "Optional override for the network security group name."
  type        = string
  default     = null
}

variable "nat_gateway_name" {
  description = "Optional override for the NAT gateway name."
  type        = string
  default     = null
}

variable "nat_public_ip_name" {
  description = "Optional override for the NAT gateway public IP name."
  type        = string
  default     = null
}

variable "vmss_name" {
  description = "Optional override for the VM scale set name."
  type        = string
  default     = null
}

variable "computer_name_prefix" {
  description = "Optional Windows computer name prefix. Defaults to a truncated form of name_prefix."
  type        = string
  default     = null

  validation {
    condition     = var.computer_name_prefix == null ? true : length(var.computer_name_prefix) <= 15
    error_message = "computer_name_prefix must be 15 characters or fewer for Windows."
  }
}

variable "vnet_address_space" {
  description = "Address space for the worker virtual network."
  type        = list(string)
  default     = ["10.42.0.0/16"]
}

variable "subnet_address_prefixes" {
  description = "Address prefixes for the worker subnet."
  type        = list(string)
  default     = ["10.42.1.0/24"]
}

variable "subnet_default_outbound_access_enabled" {
  description = "Whether to allow Azure default outbound access on the worker subnet."
  type        = bool
  default     = false
}

variable "create_nat_gateway" {
  description = "Create and attach a NAT gateway for explicit outbound internet access."
  type        = bool
  default     = true
}

variable "nat_idle_timeout_in_minutes" {
  description = "Idle timeout for the NAT gateway."
  type        = number
  default     = 10
}

variable "admin_username" {
  description = "Local administrator username for Windows VM instances."
  type        = string
  default     = "runneradmin"
}

variable "admin_password" {
  description = "Optional local administrator password override. If null, the root reads the password from Key Vault."
  type        = string
  default     = null
  sensitive   = true
}

variable "key_vault_name" {
  description = "Shared Key Vault name used for Terraform data-source secret reads."
  type        = string
}

variable "key_vault_resource_group_name" {
  description = "Resource group containing the shared Key Vault."
  type        = string
  default     = "infra"
}

variable "key_vault_subscription_id" {
  description = "Optional Azure subscription ID containing the shared Key Vault. When null, the deployment subscription is used."
  type        = string
  default     = null
}

variable "admin_password_secret_name" {
  description = "Key Vault secret name containing the local admin password."
  type        = string
  default     = "card-utility-stats-vm-admin-password"
}

variable "enable_vmss" {
  description = "Whether to create the Windows VM scale set."
  type        = bool
  default     = true
}

variable "enable_issue_agent_runner_bootstrap" {
  description = "Whether to register each VMSS instance as a GitHub self-hosted runner on first boot."
  type        = bool
  default     = false
}

variable "issue_agent_repository_slug" {
  description = "GitHub owner/repository slug used for self-hosted runner registration."
  type        = string
  default     = "nelsong6/card-utility-stats"

  validation {
    condition     = can(regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", var.issue_agent_repository_slug))
    error_message = "issue_agent_repository_slug must be in owner/repository form."
  }
}

variable "issue_agent_runner_pat_secret_name" {
  description = "Key Vault secret name containing the GitHub PAT used to mint runner registration tokens."
  type        = string
  default     = "github-pat"
}

variable "issue_agent_runner_labels" {
  description = "Additional labels to register on each GitHub self-hosted runner."
  type        = list(string)
  default     = ["issue-agent"]

  validation {
    condition     = length([for label in var.issue_agent_runner_labels : trimspace(label) if trimspace(label) != ""]) > 0
    error_message = "issue_agent_runner_labels must contain at least one non-empty label."
  }
}

variable "issue_agent_runner_group" {
  description = "Optional GitHub runner group for organization- or enterprise-level runner registration. Leave null for repository-scoped runners."
  type        = string
  default     = null
}

variable "issue_agent_runner_name_prefix" {
  description = "Prefix used when deriving the GitHub runner name from the Windows computer name."
  type        = string
  default     = "issue-agent"
}

variable "issue_agent_runner_root" {
  description = "Filesystem path containing the unpacked GitHub Actions runner files inside the image."
  type        = string
  default     = "D:\\actions-runner"
}

variable "issue_agent_runner_script_ref" {
  description = "Git ref used when downloading the first-boot runner bootstrap script from raw GitHub."
  type        = string
  default     = "main"
}

variable "vm_sku" {
  description = "Azure VM size for the scale set instances."
  type        = string
  default     = "Standard_D4s_v5"
}

variable "instance_count" {
  description = "Number of VMSS instances to create."
  type        = number
  default     = 1

  validation {
    condition     = var.instance_count >= 0
    error_message = "instance_count must be 0 or greater."
  }
}

variable "zones" {
  description = "Optional availability zones for resources that support them."
  type        = list(string)
  default     = []
}

variable "os_disk_storage_account_type" {
  description = "Managed disk SKU for the VMSS OS disks."
  type        = string
  default     = "StandardSSD_LRS"
}

variable "os_disk_caching" {
  description = "Caching mode for the VMSS OS disks."
  type        = string
  default     = "ReadWrite"
}

variable "os_disk_size_gb" {
  description = "OS disk size in GiB."
  type        = number
  default     = 127
}

variable "source_image_id" {
  description = "Optional custom image or shared image gallery version ID for the VMSS."
  type        = string
  default     = null
}

variable "vmss_image_os_state" {
  description = "Whether the VMSS source image is generalized or specialized."
  type        = string
  default     = "generalized"

  validation {
    condition     = contains(["generalized", "specialized"], var.vmss_image_os_state)
    error_message = "vmss_image_os_state must be generalized or specialized."
  }
}

variable "marketplace_image" {
  description = "Marketplace image to use when source_image_id is not provided."
  type = object({
    publisher = string
    offer     = string
    sku       = string
    version   = string
  })
  default = {
    publisher = "MicrosoftWindowsServer"
    offer     = "WindowsServer"
    sku       = "2022-datacenter-azure-edition"
    version   = "latest"
  }
}

variable "enable_builder_vm" {
  description = "Whether to create a standalone builder VM for the one-time Steam and STS2 image bootstrap."
  type        = bool
  default     = false
}

variable "builder_vm_name" {
  description = "Optional override for the builder VM name."
  type        = string
  default     = null
}

variable "builder_nic_name" {
  description = "Optional override for the builder VM NIC name."
  type        = string
  default     = null
}

variable "builder_public_ip_name" {
  description = "Optional override for the builder VM public IP name."
  type        = string
  default     = null
}

variable "builder_public_ip_dns_label" {
  description = "Optional Azure-managed DNS label for the builder VM public IP. When set, Azure publishes an FQDN for the builder public IP."
  type        = string
  default     = null
}

variable "builder_computer_name" {
  description = "Optional Windows computer name for the builder VM. Defaults to a truncated form of the builder VM name."
  type        = string
  default     = null

  validation {
    condition     = var.builder_computer_name == null ? true : length(var.builder_computer_name) <= 15
    error_message = "builder_computer_name must be 15 characters or fewer for Windows."
  }
}

variable "builder_vm_sku" {
  description = "Azure VM size for the standalone builder VM."
  type        = string
  default     = "Standard_D4s_v5"
}

variable "builder_source_image_id" {
  description = "Optional custom image or shared image gallery version ID for the builder VM."
  type        = string
  default     = null
}

variable "builder_marketplace_image" {
  description = "Marketplace image to use for the builder VM when builder_source_image_id is not provided."
  type = object({
    publisher = string
    offer     = string
    sku       = string
    version   = string
  })
  default = {
    publisher = "MicrosoftWindowsServer"
    offer     = "WindowsServer"
    sku       = "2022-datacenter-azure-edition"
    version   = "latest"
  }
}

variable "upgrade_mode" {
  description = "VMSS upgrade mode."
  type        = string
  default     = "Manual"

  validation {
    condition     = contains(["Automatic", "Manual", "Rolling"], var.upgrade_mode)
    error_message = "upgrade_mode must be Automatic, Manual, or Rolling."
  }
}

variable "encryption_at_host_enabled" {
  description = "Enable encryption at host for VMSS instances when the Azure subscription has the required feature enabled."
  type        = bool
  default     = false
}

variable "secure_boot_enabled" {
  description = "Enable secure boot when the selected image supports Trusted Launch."
  type        = bool
  default     = false
}

variable "vtpm_enabled" {
  description = "Enable vTPM when the selected image supports Trusted Launch."
  type        = bool
  default     = false
}

variable "enable_rdp_rule" {
  description = "Add an NSG rule allowing inbound RDP from the effective CIDR list."
  type        = bool
  default     = false
}

variable "rdp_allowed_cidrs" {
  description = "Explicit CIDRs allowed to reach RDP. When empty, the root may load the CIDRs from Key Vault."
  type        = list(string)
  default     = []
}

variable "rdp_allowed_cidrs_secret_name" {
  description = "Optional Key Vault secret name containing a JSON array of RDP allowlist CIDRs."
  type        = string
  default     = "card-utility-stats-rdp-allowed-cidrs"
}

variable "enable_winrm_rule" {
  description = "Add an NSG rule allowing inbound WinRM over HTTPS from the effective CIDR list."
  type        = bool
  default     = false
}

variable "winrm_allowed_cidrs" {
  description = "Explicit CIDRs allowed to reach WinRM over HTTPS on port 5986. When empty, the root may load the CIDRs from Key Vault."
  type        = list(string)
  default     = []
}

variable "winrm_allowed_cidrs_secret_name" {
  description = "Optional Key Vault secret name containing a JSON array of WinRM allowlist CIDRs."
  type        = string
  default     = null
}

variable "tags" {
  description = "Additional tags to apply to Azure resources."
  type        = map(string)
  default     = {}
}
