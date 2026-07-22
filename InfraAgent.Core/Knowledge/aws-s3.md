# AWS S3 Terraform guidance

Use aws_s3_bucket for the bucket resource. If the user supplies a bucket name, preserve it exactly in the generated Terraform; never replace it with an example name.

Enable aws_s3_bucket_public_access_block with all four blocking settings true. Use aws_s3_bucket_server_side_encryption_configuration with AES256 by default. Use aws_s3_bucket_versioning when versioning is requested. Do not create public-read ACLs or public bucket policies.
