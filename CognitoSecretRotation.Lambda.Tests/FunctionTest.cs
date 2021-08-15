using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using CognitoSecretRotation.Lambda.Models;
using CognitoSecretRotation.Lambda.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitoSecretRotation.Lambda.Tests
{
    public class FunctionTest
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<IAmazonSecretsManager> _secretsManagerClient;
        private readonly Mock<ISecretRotationService> _secretRotationService;

        public FunctionTest()
        {
            _secretsManagerClient = new Mock<IAmazonSecretsManager>();
            _secretRotationService = new Mock<ISecretRotationService>();
            _serviceProvider = new ServiceCollection()
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddScoped<IAmazonSecretsManager>(x => _secretsManagerClient.Object)
                .AddScoped<ISecretRotationService>(x => _secretRotationService.Object)
                .BuildServiceProvider();
        }

        [Fact]
        public async Task HandleAsync_SecretWithDisabledRotation_ThrowsException()
        {
            var request = new RotationRequest()
            {
                SecretId = "secretId"
            };

            var describeSecretResponse = new DescribeSecretResponse()
            {
                RotationEnabled = false
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(request.SecretId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var func = new Function(_serviceProvider);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => func.HandleAsync(request, null));

            _secretRotationService.VerifyNoOtherCalls();
        }


        [Fact]
        public async Task HandleAsync_SecretHasNoStageForRotation_ThrowsException()
        {
            var request = new RotationRequest()
            {
                SecretId = "secretId",
                ClientRequestToken = "token"
            };

            var describeSecretResponse = new DescribeSecretResponse()
            {
                RotationEnabled = true,
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {"other_token", new List<string>()}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(request.SecretId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var func = new Function(_serviceProvider);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => func.HandleAsync(request, null));

            _secretRotationService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task HandleAsync_SecretAlreadySetAsCurrent_DoesNothing()
        {
            var request = new RotationRequest()
            {
                SecretId = "secretId",
                ClientRequestToken = "token"
            };

            var describeSecretResponse = new DescribeSecretResponse()
            {
                RotationEnabled = true,
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {request.ClientRequestToken, new List<string>() {Consts.AwsCurrent}}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(request.SecretId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var func = new Function(_serviceProvider);

            // Act
            await func.HandleAsync(request, null);

            // Assert
            _secretRotationService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task HandleAsync_SecretNotSetAsPending_ThrowsException()
        {
            var request = new RotationRequest()
            {
                SecretId = "secretId",
                ClientRequestToken = "token"
            };

            var describeSecretResponse = new DescribeSecretResponse()
            {
                RotationEnabled = true,
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {request.ClientRequestToken, new List<string>()}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(request.SecretId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var func = new Function(_serviceProvider);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => func.HandleAsync(request, null));

            _secretRotationService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task HandleAsync_InvalidStep_ThrowsException()
        {
            var request = new RotationRequest()
            {
                SecretId = "secretId",
                ClientRequestToken = "token",
                Step = "invalidStep"
            };

            var describeSecretResponse = new DescribeSecretResponse()
            {
                RotationEnabled = true,
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {request.ClientRequestToken, new List<string>() {Consts.AwsPending}}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(request.SecretId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var func = new Function(_serviceProvider);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => func.HandleAsync(request, null));

            _secretRotationService.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("createSecret")]
        [InlineData("setSecret")]
        [InlineData("testSecret")]
        [InlineData("finishSecret")]
        public async Task HandleAsync_ValidSecretWithStep_ExecutesCorrectStep(string step)
        {
            var request = new RotationRequest()
            {
                SecretId = "secretId",
                ClientRequestToken = "token",
                Step = step
            };

            var describeSecretResponse = new DescribeSecretResponse()
            {
                RotationEnabled = true,
                VersionIdsToStages = new Dictionary<string, List<string>>()
                {
                    {request.ClientRequestToken, new List<string>() {Consts.AwsPending}}
                }
            };

            _secretsManagerClient.Setup(x => x.DescribeSecretAsync(
                    It.Is<DescribeSecretRequest>(r => r.SecretId.Equals(request.SecretId)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(describeSecretResponse);

            var func = new Function(_serviceProvider);

            // Act
            await func.HandleAsync(request, null);

            // Assert
            Assert.Equal(1, _secretRotationService.Invocations.Count);
            Assert.Equal(step, _secretRotationService.Invocations[0].Method.Name, true);
        }
    }
}