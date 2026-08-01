terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }

  # Configure with: terraform init -backend-config=../../backend.hcl
  # (override key per env in backend partial or -backend-config="key=...")
  backend "s3" {}
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      env     = var.environment
      owner   = var.owner
      project = "hrmanagement"
      managed = "terraform-ops005"
    }
  }
}

variable "aws_region" {
  type    = string
  default = "eu-central-1"
}

variable "environment" {
  type = string
}

variable "owner" {
  type    = string
  default = "platform"
}

variable "name_prefix" {
  type = string
}

variable "vpc_cidr" {
  type = string
}

variable "backup_bucket_name" {
  type = string
}

variable "cluster_version" {
  type    = string
  default = "1.29"
}

variable "node_desired" {
  type    = number
  default = 3
}

variable "dns_enabled" {
  type    = bool
  default = false
}

variable "route53_zone_id" {
  type    = string
  default = ""
}

variable "dns_record_name" {
  type    = string
  default = ""
}

variable "dns_record_value" {
  type    = string
  default = "placeholder.example.invalid"
}

locals {
  tags = {
    env   = var.environment
    owner = var.owner
  }
}

module "network" {
  source      = "../../modules/network"
  name_prefix = var.name_prefix
  cidr        = var.vpc_cidr
  tags        = local.tags
}

module "eks" {
  source          = "../../modules/eks"
  name_prefix     = var.name_prefix
  cluster_version = var.cluster_version
  subnet_ids      = module.network.private_subnet_ids
  node_desired    = var.node_desired
  tags            = local.tags
}

module "object_store" {
  source      = "../../modules/object_store"
  name_prefix = var.name_prefix
  bucket_name = var.backup_bucket_name
  tags        = local.tags
}

module "dns" {
  source       = "../../modules/dns"
  enabled      = var.dns_enabled
  zone_id      = var.route53_zone_id
  record_name  = var.dns_record_name
  record_value = var.dns_record_value
  tags         = local.tags
}

data "tls_certificate" "eks" {
  url = module.eks.oidc_issuer
}

resource "aws_iam_openid_connect_provider" "eks" {
  client_id_list  = ["sts.amazonaws.com"]
  thumbprint_list = [data.tls_certificate.eks.certificates[0].sha1_fingerprint]
  url             = module.eks.oidc_issuer
  tags            = local.tags
}

module "secrets_bridge" {
  source            = "../../modules/secrets_bridge"
  name_prefix       = var.name_prefix
  oidc_provider_arn = aws_iam_openid_connect_provider.eks.arn
  tags              = local.tags
}

output "vpc_id" {
  value = module.network.vpc_id
}

output "cluster_name" {
  value = module.eks.cluster_name
}

output "cluster_endpoint" {
  value = module.eks.cluster_endpoint
}

output "oidc_issuer" {
  value = module.eks.oidc_issuer
}

output "backup_bucket" {
  value = module.object_store.bucket_id
}

output "eso_role_arn" {
  value = module.secrets_bridge.role_arn
}

output "dns_fqdn" {
  value = module.dns.fqdn
}
