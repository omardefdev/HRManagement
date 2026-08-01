terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

variable "name_prefix" {
  type = string
}

variable "oidc_provider_arn" {
  type = string
}

variable "namespace" {
  type    = string
  default = "external-secrets"
}

variable "service_account" {
  type    = string
  default = "external-secrets"
}

variable "tags" {
  type    = map(string)
  default = {}
}

locals {
  # arn:aws:iam::ACCOUNT:oidc-provider/HOST → HOST
  oidc_host = split(":oidc-provider/", var.oidc_provider_arn)[1]
}

data "aws_iam_policy_document" "assume" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]
    principals {
      type        = "Federated"
      identifiers = [var.oidc_provider_arn]
    }
    condition {
      test     = "StringEquals"
      variable = "${local.oidc_host}:sub"
      values   = ["system:serviceaccount:${var.namespace}:${var.service_account}"]
    }
  }
}

resource "aws_iam_role" "eso" {
  name               = "${var.name_prefix}-eso"
  assume_role_policy = data.aws_iam_policy_document.assume.json
  tags               = var.tags
}

data "aws_iam_policy_document" "eso" {
  statement {
    sid = "ReadSecrets"
    actions = [
      "secretsmanager:GetSecretValue",
      "secretsmanager:DescribeSecret",
      "ssm:GetParameter",
      "ssm:GetParameters",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "eso" {
  name   = "${var.name_prefix}-eso-read"
  role   = aws_iam_role.eso.id
  policy = data.aws_iam_policy_document.eso.json
}

output "role_arn" {
  value = aws_iam_role.eso.arn
}
