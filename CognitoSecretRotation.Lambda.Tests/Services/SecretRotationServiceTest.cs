using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AutoMapper;
using CognitoSecretRotation.Lambda.Models;
using CognitoSecretRotation.Lambda.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ResourceNotFoundException = Amazon.SecretsManager.Model.ResourceNotFoundException;

namespace CognitoSecretRotation.Lambda.Tests.Services
{
    public class SecretRotationServiceTest
    {
        const string Arn = "arn";
        const string Token = "token";

        private readonly Mock<ILogger<SecretRotationService>> _logger;
        private readonly Mock<IApiAuthenticationService> _apiAuthenticationService;
        private readonly Mock<IAmazonSecretsManager> _secretsManagerClient;
        private readonly Mock<IAmazonCognitoIdentityProvider> _cognitoClient;
        private readonly Mock<IMapper> _mapper;
        private readonly string _userPoolId;


        public SecretRotationServiceTest()
        {
            _logger = new Mock<ILogger<SecretRotationService>>();
            _apiAuthenticationService = new Mock<IApiAuthenticationService>();
            _secretsManagerClient = new Mock<IAmazonSecretsManager>();
            _cognitoClient = new Mock<IAmazonCognitoIdentityProvider>();
            _mapper = new Mock<IMapper>();
            _userPoolId = "user_pool_id";
        }

        [Fact]
        public async Task CreateSecret_PendingSecretAlreadyExists_SkipsCreatingNew()
        {
            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.CreateSecret(Arn, Token);

            // Assert
            _secretsManagerClient.Verify(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r => r.VersionStage.Equals(Consts.AwsPending)),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _cognitoClient.Verify(x => x.CreateUserPoolClientAsync(
                    It.IsAny<CreateUserPoolClientRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            _secretsManagerClient.Verify(x => x.PutSecretValueAsync(
                    It.IsAny<PutSecretValueRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateSecret_PendingSecretDoesNotExist_CreatesPendingSecretAndAppClient()
        {
            _secretsManagerClient.Setup(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r => r.VersionStage.Equals(Consts.AwsPending)),
                    It.IsAny<CancellationToken>()))
                .Throws(new ResourceNotFoundException("test"));

            _secretsManagerClient.Setup(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r => r.SecretId.Equals(Arn) && r.VersionStage.Equals(Consts.AwsCurrent)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse() { SecretString = "{\"clientId\":\"123\"}" });

            var cognitoResponse = new CreateUserPoolClientResponse()
            {
                UserPoolClient = new UserPoolClientType()
                {
                    ClientId = "clientId",
                    ClientSecret = "clientSecret"
                }
            };
            _cognitoClient.Setup(x => x.CreateUserPoolClientAsync(
                    It.IsAny<CreateUserPoolClientRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cognitoResponse);

            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.CreateSecret(Arn, Token);

            // Assert
            _cognitoClient.Verify(x => x.CreateUserPoolClientAsync(
                    It.IsAny<CreateUserPoolClientRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _secretsManagerClient.Verify(x => x.PutSecretValueAsync(
                    It.IsAny<PutSecretValueRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SetSecret_Secret_DoesNothing()
        {
            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.SetSecret(Arn, Token);

            // Assert
            _secretsManagerClient.VerifyNoOtherCalls();
            _cognitoClient.VerifyNoOtherCalls();
            _apiAuthenticationService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task TestSecret_CorrectPendingSecret_ExecutesWithoutException()
        {
            _secretsManagerClient.Setup(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r => r.SecretId.Equals(Arn) && r.VersionId.Equals(Token)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse() { SecretString = "{\"clientId\":\"123\"}" });

            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.TestSecret(Arn, Token);

            // Assert
            _secretsManagerClient.Verify(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r => r.SecretId.Equals(Arn) && r.VersionId.Equals(Token)),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _apiAuthenticationService.Verify(x => x.RetrieveTokenAsync(
                    It.IsAny<Credentials>()),
                Times.Once);
        }

        [Fact]
        public async Task TestSecret_IncorrectPendingSecret_ThrowsException()
        {
            _secretsManagerClient.Setup(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r => r.SecretId.Equals(Arn) && r.VersionId.Equals(Token)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse() { SecretString = "{\"clientId\":\"123\"}" });

            _apiAuthenticationService.Setup(x => x.RetrieveTokenAsync(It.IsAny<Credentials>()))
                .Throws<HttpRequestException>();

            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await Assert.ThrowsAsync<HttpRequestException>(() => service.TestSecret(Arn, Token));

            // Assert
            _secretsManagerClient.Verify(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r => r.SecretId.Equals(Arn) && r.VersionId.Equals(Token)),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _apiAuthenticationService.Verify(x => x.RetrieveTokenAsync(
                    It.IsAny<Credentials>()),
                Times.Once);
        }

        [Fact]
        public async Task FinishSecret_SecretAlreadyInCurrentStage_DoesNothing()
        {
            var describeSecretResponse = new DescribeSecretResponse()
            {
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {Token, new List<string>() {Consts.AwsCurrent}}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(Arn)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.FinishSecret(Arn, Token);

            // Assert
            _secretsManagerClient.Verify(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(Arn)),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _secretsManagerClient.Verify(x => x.UpdateSecretVersionStageAsync(
                    It.IsAny<UpdateSecretVersionStageRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task FinishSecret_PendingSecret_MovesPendingSecretToCurrentStageAndDeletesPendingStageFromIt()
        {
            var describeSecretResponse = new DescribeSecretResponse()
            {
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {"other_token", new List<string>() {Consts.AwsCurrent}}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(Arn)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.FinishSecret(Arn, Token);

            // Assert
            _secretsManagerClient.Verify(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(Arn)),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            _secretsManagerClient.Verify(x => x.UpdateSecretVersionStageAsync(
                    It.IsAny<UpdateSecretVersionStageRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task FinishSecret_ExistingPreviousSecret_DeletesAppClient()
        {
            const string clientId = "123";
            var describeSecretResponse = new DescribeSecretResponse()
            {
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {"other_token", new List<string>() {Consts.AwsCurrent}}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(Arn)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            _secretsManagerClient.Setup(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r =>
                        r.SecretId.Equals(Arn) && r.VersionStage.Equals(Consts.AwsPrevious)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse() {SecretString = "{\"clientId\":\"" + clientId + "\"}"});

            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.FinishSecret(Arn, Token);

            // Assert
            _cognitoClient.Verify(x => x.DeleteUserPoolClientAsync(
                    It.Is<DeleteUserPoolClientRequest>(r =>
                        r.UserPoolId.Equals(_userPoolId) && r.ClientId.Equals(clientId)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task FinishSecret_NoPreviousSecret_DoesNotDeleteAppClient()
        {
            var describeSecretResponse = new DescribeSecretResponse()
            {
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {"other_token", new List<string>() {Consts.AwsCurrent}}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(Arn)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            _secretsManagerClient.Setup(x => x.GetSecretValueAsync(
                    It.Is<GetSecretValueRequest>(r =>
                        r.SecretId.Equals(Arn) && r.VersionStage.Equals(Consts.AwsPrevious)),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ResourceNotFoundException("test"));

            var service = new SecretRotationService(
                _logger.Object,
                _apiAuthenticationService.Object,
                _cognitoClient.Object,
                _secretsManagerClient.Object,
                _mapper.Object,
                _userPoolId);

            // Act
            await service.FinishSecret(Arn, Token);

            // Assert
            _cognitoClient.Verify(x => x.DeleteUserPoolClientAsync(
                    It.IsAny<DeleteUserPoolClientRequest>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}