namespace CognitoSecretRotation.Lambda.Models
{
    public class Credentials
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Scope { get; set; }
    }
}