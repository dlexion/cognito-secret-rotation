using System;
using System.Threading.Tasks;
using Amazon.CognitoIdentityProvider;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AutoMapper;
using CognitoSecretRotation.Lambda.Mappers;
using CognitoSecretRotation.Lambda.Models;
using CognitoSecretRotation.Lambda.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CognitoSecretRotation.Lambda
{
    public class Function
    {
        private readonly IServiceProvider _serviceProvider;

        public Function()
        {
            var host = CreateHostBuilder().Build();
            _serviceProvider = host.Services;
        }

        public Function(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task HandleAsync(RotationRequest request, ILambdaContext context)
        {
            using var scope = _serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetService<ILogger<Function>>();

            logger.LogInformation("Received input: {@request}", request);

            using var secretsManager = scope.ServiceProvider.GetRequiredService<IAmazonSecretsManager>();

            var metadata = await secretsManager.DescribeSecretAsync(
                new DescribeSecretRequest() { SecretId = request.SecretId });

            if (!metadata.RotationEnabled)
            {
                logger.LogError($"Secret {request.SecretId} is not enabled for rotation");
                throw new ArgumentException($"Secret {request.SecretId} is not enabled for rotation",
                    nameof(metadata.RotationEnabled));
            }

            var versions = metadata.VersionIdsToStages;

            if (!versions.ContainsKey(request.ClientRequestToken))
            {
                logger.LogError(
                    $"Secret version {request.ClientRequestToken} has no stage for rotation of secret {request.SecretId}");
                throw new ArgumentException(
                    $"Secret version {request.ClientRequestToken} has no stage for rotation of secret {request.SecretId}",
                    nameof(metadata.VersionIdsToStages));
            }

            if (versions[request.ClientRequestToken].Contains(Consts.AwsCurrent))
            {
                logger.LogInformation(
                    $"Secret version {request.ClientRequestToken} already set as {Consts.AwsCurrent} for secret {request.SecretId}.");
                return;
            }

            if (!versions[request.ClientRequestToken].Contains(Consts.AwsPending))
            {
                logger.LogError(
                    $"Secret version {request.ClientRequestToken} not set as {Consts.AwsPending} for rotation of secret {request.SecretId}.");
                throw new ArgumentException(
                    $"Secret version {request.ClientRequestToken} not set as {Consts.AwsPending} for rotation of secret {request.SecretId}.",
                    nameof(metadata.VersionIdsToStages));
            }

            logger.LogInformation($"Performing step: {request.Step}");
            var secretRotationService = scope.ServiceProvider.GetRequiredService<ISecretRotationService>();

            switch (request.Step)
            {
                case "createSecret":
                    await secretRotationService.CreateSecret(request.SecretId, request.ClientRequestToken);
                    break;
                case "setSecret":
                    await secretRotationService.SetSecret(request.SecretId, request.ClientRequestToken);
                    break;
                case "testSecret":
                    await secretRotationService.TestSecret(request.SecretId, request.ClientRequestToken);
                    break;
                case "finishSecret":
                    await secretRotationService.FinishSecret(request.SecretId, request.ClientRequestToken);
                    break;
                default:
                    throw new ArgumentException("Invalid step parameter", nameof(request.Step));
            }
        }

        private static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .UseSerilog((context, configuration) =>
                    configuration.Enrich.FromLogContext()
                        .WriteTo.Console(new CompactJsonFormatter(), LogEventLevel.Information))
                .ConfigureAppConfiguration(builder =>
                {
                    builder.AddSystemsManager("/licensing");
                })
                .ConfigureServices((context, services) => { ConfigureServices(context.Configuration, services); });

        private static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
        {
            services.AddAutoMapper(typeof(LicenseMappingProfile));

            var authorizationUrl = Environment.GetEnvironmentVariable("AuthorizationUrl");
            services.AddHttpClient<IApiAuthenticationService, ApiAuthenticationService>()
                .ConfigureHttpClient(c => c.BaseAddress = new Uri(authorizationUrl));

            services.AddScoped<IAmazonSecretsManager, AmazonSecretsManagerClient>();
            services.AddScoped<IAmazonCognitoIdentityProvider, AmazonCognitoIdentityProviderClient>();

            var userPoolId = configuration.GetValue<string>("userPoolId");
            services.AddScoped<ISecretRotationService, SecretRotationService>(x => new SecretRotationService(
                x.GetRequiredService<ILogger<SecretRotationService>>(),
                x.GetRequiredService<IApiAuthenticationService>(),
                x.GetRequiredService<IAmazonCognitoIdentityProvider>(),
                x.GetRequiredService<IAmazonSecretsManager>(),
                x.GetRequiredService<IMapper>(),
                userPoolId));
        }
    }
}
