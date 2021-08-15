using System.Threading.Tasks;
using Amazon.SecretsManager;

namespace CognitoSecretRotation.Lambda.Services
{
    public interface ISecretRotationService
    {
        public Task CreateSecret(string arn, string token);

        public Task SetSecret(string arn, string token);

        public Task TestSecret(string arn, string token);

        public Task FinishSecret(string arn, string token);
    }
}