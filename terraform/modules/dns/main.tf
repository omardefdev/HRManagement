terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

variable "enabled" {
  type    = bool
  default = false
}

variable "zone_id" {
  type    = string
  default = ""
}

variable "record_name" {
  type    = string
  default = ""
}

variable "record_type" {
  type    = string
  default = "CNAME"
}

variable "record_value" {
  type    = string
  default = "placeholder.example.invalid"
}

variable "ttl" {
  type    = number
  default = 60
}

variable "tags" {
  type    = map(string)
  default = {}
}

resource "aws_route53_record" "app" {
  count   = var.enabled && var.zone_id != "" ? 1 : 0
  zone_id = var.zone_id
  name    = var.record_name
  type    = var.record_type
  ttl     = var.ttl
  records = [var.record_value]
}

output "fqdn" {
  value = try(aws_route53_record.app[0].fqdn, "")
}
