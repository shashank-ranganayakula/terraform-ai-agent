# AWS EC2 Terraform guidance

Use data.aws_ami to select the latest Amazon Linux 2023 AMI owned by Amazon. Preserve the instance type and instance name supplied by the user.

Do not create unrestricted ingress rules. Reject or clarify requests for 0.0.0.0/0 or ::/0. Require an explicit CIDR for SSH, HTTP, or HTTPS ingress.
