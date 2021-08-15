using System.Threading.Tasks;
using CognitoSecretRotation.Lambda.Models;

namespace CognitoSecretRotation.Lambda.Services
{
    public interface IApiAuthenticationService
    {
        Task<string> RetrieveTokenAsync(Credentials credentials);
    }
}