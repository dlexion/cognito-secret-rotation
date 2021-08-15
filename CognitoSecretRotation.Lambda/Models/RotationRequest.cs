namespace CognitoSecretRotation.Lambda.Models
{
    public class RotationRequest
    {
        public string Step { get; set; }

        public string SecretId { get; set; }

        public string ClientRequestToken { get; set; }
    }
}