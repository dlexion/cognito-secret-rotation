using System;
using System.Net.Http;
using System.Threading.Tasks;
using CognitoSecretRotation.Lambda.Models;
using IdentityModel.Client;

namespace CognitoSecretRotation.Lambda.Services
{
    public class ApiAuthenticationService : IApiAuthenticationService
    {
        private readonly HttpClient _httpClient;

        public ApiAuthenticationService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> RetrieveTokenAsync(Credentials credentials)
        {
            var response = await _httpClient.RequestClientCredentialsTokenAsync(
                new ClientCredentialsTokenRequest
                {
                    ClientId = credentials.ClientId,
                    ClientSecret = credentials.ClientSecret,
                    Scope = credentials.Scope
                });

            response.HttpResponse.EnsureSuccessStatusCode();

            var token = response.AccessToken;

            return token;
        }
    }
}