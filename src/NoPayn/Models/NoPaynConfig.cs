namespace NoPayn.Models;

public record NoPaynConfig(
    string ApiKey,
    string MerchantId,
    string BaseUrl = "https://api.nopayn.co.uk"
);
