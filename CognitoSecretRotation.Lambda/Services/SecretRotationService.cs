using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AutoMapper;
using CognitoSecretRotation.Lambda.Models;
using Microsoft.Extensions.Logging;
using ResourceNotFoundException = Amazon.SecretsManager.Model.ResourceNotFoundException;

namespace CognitoSecretRotation.Lambda.Services
{
    public class SecretRotationService : ISecretRotationService
    {
        private readonly ILogger<SecretRotationService> _logger;
        private readonly IApiAuthenticationService _apiAuthenticationService;
        private readonly IAmazonCognitoIdentityProvider _cognitoClient;
        private readonly IAmazonSecretsManager _secretsManagerClient;
        private readonly IMapper _mapper;
        private readonly string _userPoolId;

        public SecretRotationService(
            ILogger<SecretRotationService> logger,
            IApiAuthenticationService apiAuthenticationService,
            IAmazonCognitoIdentityProvider cognitoClient,
            IAmazonSecretsManager secretsManagerClient,
            IMapper mapper,
            string userPoolId)
        {
            _logger = logger;
            _apiAuthenticationService = apiAuthenticationService;
            _cognitoClient = cognitoClient;
            _secretsManagerClient = secretsManagerClient;
            _mapper = mapper;
            _userPoolId = userPoolId;
        }

        public async Task CreateSecret(string arn, string token)
        {
            // Make sure the current secret exists
            var currentSecret = await _secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest() { SecretId = arn, VersionStage = Consts.AwsCurrent });

            try
            {
                await _secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest()
                { SecretId = arn, VersionId = token, VersionStage = Consts.AwsPending });
                _logger.LogInformation($"createSecret: Successfully retrieved secret for {arn}.");
            }
            catch (ResourceNotFoundException)
            {
                var credentials = DeserializeCredentials(currentSecret.SecretString);

                var currentAppClient = await _cognitoClient.DescribeUserPoolClientAsync(new DescribeUserPoolClientRequest()
                {
                    UserPoolId = _userPoolId,
                    ClientId = credentials.ClientId
                });

                var createClientRequest = _mapper.Map<CreateUserPoolClientRequest>(currentAppClient);

                var createClientResponse = await _cognitoClient.CreateUserPoolClientAsync(createClientRequest);

                _logger.LogInformation($"createSecret: Successfully create Application client {createClientResponse.UserPoolClient.ClientId}");

                var serializedSecrets = SerializeCredentials(
                    new Credentials
                    {
                        ClientId = createClientResponse.UserPoolClient.ClientId,
                        ClientSecret = createClientResponse.UserPoolClient.ClientSecret,
                        Scope = string.Join(' ', createClientResponse.UserPoolClient.AllowedOAuthScopes)
                    });

                await _secretsManagerClient.PutSecretValueAsync(new PutSecretValueRequest()
                {
                    SecretId = arn,
                    ClientRequestToken = token,
                    SecretString = serializedSecrets,
                    VersionStages = new[] { Consts.AwsPending }.ToList()
                });

                _logger.LogInformation($"createSecret: Successfully put secret for ARN {arn} and version {token}");
            }
        }

        public async Task SetSecret(string arn, string token)
        {
            _logger.LogInformation("setSecret: Secrets are auto-generated during creation of cognito app client in createSecret step. No action needed.");
            await Task.CompletedTask;
        }

        public async Task TestSecret(string arn, string token)
        {
            var pendingSecret = await _secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest()
            {
                SecretId = arn,
                VersionId = token,
            });

            var credentials = DeserializeCredentials(pendingSecret.SecretString);

            await _apiAuthenticationService.RetrieveTokenAsync(credentials);
            _logger.LogInformation($"testSecret: Successfully test {Consts.AwsPending} secret for ARN {arn} and version {token}");
        }

        public async Task FinishSecret(string arn, string token)
        {
            var metadata = await _secretsManagerClient.DescribeSecretAsync(
                new DescribeSecretRequest() { SecretId = arn });

            var currentVersion = metadata.VersionIdsToStages.SingleOrDefault(x => x.Value.Contains(Consts.AwsCurrent)).Key;

            if (currentVersion == token)
            {
                _logger.LogInformation($"finishSecret: Version {currentVersion} already marked as {Consts.AwsCurrent} for {arn}");
                return;
            }

            try
            {
                var previousSecret = await _secretsManagerClient.GetSecretValueAsync(
                    new GetSecretValueRequest() {SecretId = arn, VersionStage = Consts.AwsPrevious});

                var credentials = DeserializeCredentials(previousSecret.SecretString);

                await _cognitoClient.DeleteUserPoolClientAsync(new DeleteUserPoolClientRequest()
                {
                    UserPoolId = _userPoolId,
                    ClientId = credentials.ClientId,
                });

                _logger.LogInformation($"finishStep: Successfully delete Application client {credentials.ClientId}");
            }
            catch (ResourceNotFoundException)
            {
                _logger.LogInformation(
                    $"finishSecret: There is no {Consts.AwsPrevious} stage of secret. No Application client from Cognito will be removed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"finishSecret: An Error occurred during deleting of Cognito Application client for {Consts.AwsPrevious} secret stage.");
            }

            await _secretsManagerClient.UpdateSecretVersionStageAsync(new UpdateSecretVersionStageRequest()
            {
                SecretId = arn,
                VersionStage = Consts.AwsCurrent,
                MoveToVersionId = token,
                RemoveFromVersionId = currentVersion
            });

            _logger.LogInformation(
                $"finishSecret: Successfully set {Consts.AwsCurrent} stage to version {token} for secret {arn}.");

            await _secretsManagerClient.UpdateSecretVersionStageAsync(new UpdateSecretVersionStageRequest()
            {
                SecretId = arn,
                VersionStage = Consts.AwsPending,
                RemoveFromVersionId = token
            });

            _logger.LogInformation(
                $"finishSecret: Successfully remove {Consts.AwsPending} stage for version {token} for secret {arn}.");
        }

        private Credentials DeserializeCredentials(string secret)
        {
            return JsonSerializer.Deserialize<Credentials>(
                secret,
                new JsonSerializerOptions() {PropertyNamingPolicy = JsonNamingPolicy.CamelCase});
        }

        private string SerializeCredentials(Credentials credentials)
        {
            return JsonSerializer.Serialize(
                credentials,
                new JsonSerializerOptions()
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
        }
    }
}