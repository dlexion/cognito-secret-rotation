using Amazon.CognitoIdentityProvider.Model;
using AutoMapper;

namespace CognitoSecretRotation.Lambda.Mappers
{
    public class LicenseMappingProfile : Profile
    {
        public LicenseMappingProfile()
        {
            CreateMap<string, bool>()
                .ConvertUsing((s, _) => !string.IsNullOrEmpty(s));

            CreateMap<UserPoolClientType, CreateUserPoolClientRequest>()
                .ForMember(x => x.AccessTokenValidity, opt => opt.Ignore())
                .ForMember(x => x.IdTokenValidity, opt => opt.Ignore())
                .ForMember(x => x.GenerateSecret, opt => opt.MapFrom(x => x.ClientSecret));

            CreateMap<DescribeUserPoolClientResponse, CreateUserPoolClientRequest>()
                .IncludeMembers(x => x.UserPoolClient);
        }
    }
}