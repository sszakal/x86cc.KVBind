namespace x86cc.KVBind.Sample.Api.Claims;

// Validation profile for claim submission — stricter rules than the default draft profile.
public sealed record SubmitClaimValidationProfile : KVBind.Core.KVValidationProfile;
