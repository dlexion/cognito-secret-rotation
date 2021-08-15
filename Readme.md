# Secret Rotation Lambda

Lambda that is used by Secrets Manager in order to rotate secrets. It replaces Cognito Application Pool Client with new one and updates stored secrets.

### Project Files ###

* template.yml - an AWS CloudFormation Serverless Application Model template file for declaring your Serverless functions and other AWS resources
* aws-lambda-tools-defaults.json - default argument settings for use with Visual Studio and command line deployment tools for AWS
* Function.cs - class that serves as a entry point for the Lambda function

## Deployment:

To deploy the Lambda function and all associated resources you need to do the following step in consecutive order (**SAM CLI needs to be installed**):
1. sam build
2. sam package --s3-bucket licensing-service --region us-west-2 --output-template-file output_template.yml
3. sam deploy -t output_template.yml --stack-name licensing-secret-rotation --tags Environment=nonprod Service=licensing-service Capacity=BAU --region us-west-2 --capabilities CAPABILITY_NAMED_IAM

To create SSM parameter in Parameter Store run the following command:
```powershell
aws ssm put-parameter --name "/licensing/userPoolId" --value "" --type "SecureString" "Key=Environment,Value=nonprod" "Key=Service,Value=licensing-service" "Key=Capacity,Value=BAU" --region us-west-2
```

To enable rotation for secret run the following command:
```powershell
aws secretsmanager rotate-secret --secret-id licensing-credentials --rotation-lambda-arn <arn> --rotation-rules AutomaticallyAfterDays=44
```

To create example data (including Cognito Application client, Secret) and enable rotation do the following:
> **Note:** Use latest AWS CLI version. Rotation lambda assumed as already deployed. See commands above.
1. Create Cognito Application client (detailed description of command [here](https://docs.aws.amazon.com/cli/latest/reference/cognito-idp/create-user-pool-client.html)):
> Change parameters based on your needs.
```powershell
aws cognito-idp create-user-pool-client --user-pool-id us-west-2_FPrtM1JBr --client-name MyNewClient --generate-secret --no-enable-token-revocation --explicit-auth-flows "ALLOW_REFRESH_TOKEN_AUTH" --prevent-user-existence-errors "ENABLED" --allowed-o-auth-flows-user-pool-client --supported-identity-providers "COGNITO" --allowed-o-auth-flows "client_credentials" --allowed-o-auth-scopes "licensing.dev.com/license.read" --region us-west-2
```
2. Save **ClientId**, **ClientSecret**, **Scope** from response from 1st step.
3. Create Secret in SecretsManager and put saved values inside quotes to fill in the **secret-string** parameter.
> This command is for Powershell. Other shells may require changes.

```powershell
aws secretsmanager create-secret --name your-secret-name --secret-string '{\"clientId\":\"\",\"clientSecret\":\"\",\"scope\":\"\"}' --tags "Key=Environment,Value=nonprod" "Key=Service,Value=licensing-service" "Key=Capacity,Value=BAU" --region us-west-2
```
4. Enable rotation using arn of rotation lambda.
```powershell
aws secretsmanager rotate-secret --secret-id your-secret-name --rotation-lambda-arn <arn> --rotation-rules AutomaticallyAfterDays=44 --region us-west-2
```