# AWS EC2 Phase 1 Context

Use a `data "aws_ami"` lookup for the latest Amazon Linux 2023 AMI, an
`aws_security_group` associated to the instance, and an `aws_instance` resource.
Do not add ingress from `0.0.0.0/0` or `::/0`.
