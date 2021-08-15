using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel.Client;
using CognitoSecretRotation.Lambda.Models;
using CognitoSecretRotation.Lambda.Services;
using Moq;
using Moq.Protected;
using Xunit;

namespace CognitoSecretRotation.Lambda.Tests.Services
{
    public class ApiAuthenticationServiceTests
    {
        private readonly HttpClient _client;
        private readonly Mock<HttpMessageHandler> _messageHandler;

        public ApiAuthenticationServiceTests()
        {
            _messageHandler = new Mock<HttpMessageHandler>();
            _client = new HttpClient(_messageHandler.Object) {BaseAddress = new Uri(@"http:\\example.com")};
        }

        [Fact]
        public async Task RetrieveTokenAsync_ValidCredentials_ReturnsToken()
        {
            var credentials = new Credentials
            {
                ClientId = "ClientId",
                ClientSecret = "ClientSecret",
                Scope = "Scope"
            };

            _messageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("{\"access_token\":\"token\"}")
                })
                .Verifiable();

            var service = new ApiAuthenticationService(_client);

            // Act
            var token = await service.RetrieveTokenAsync(credentials);

            // Assert
            Assert.Equal("token", token);
            _messageHandler.Verify();
            Assert.Equal(1, _messageHandler.Invocations.Count);
        }

        [Fact]
        public async Task RetrieveTokenAsync_InvalidCredentials_ThrowsException()
        {
            var credentials = new Credentials
            {
                ClientId = "ClientId",
                ClientSecret = "ClientSecret",
                Scope = "Scope"
            };

            _messageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Unauthorized
                })
                .Verifiable();

            var service = new ApiAuthenticationService(_client);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() => service.RetrieveTokenAsync(credentials));
            _messageHandler.Verify();
            Assert.Equal(1, _messageHandler.Invocations.Count);
        }
    }
}