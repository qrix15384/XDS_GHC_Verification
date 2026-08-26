using System.Text.Json.Nodes;
using XDS_GHC_Verification.Models;

namespace XDS_GHC_Verification.Services;

/// <summary>
/// Transforms the raw NIA KYC response into the masked, client-facing shape
/// (N_-prefixed identity fields, X_-prefixed address history from the
/// credit API) — replacing the raw passthrough previously returned to callers.
///
/// Field-name mapping is CONFIRMED against real, live NIA responses seen
/// during integration testing, both on the failure path and — as of a real
/// successful KYC match — the full person/biometricFeed/binaries shape too
/// (see UpstreamClient/SelfieController history). Earlier revisions of this
/// file guessed some of these names before a successful match was available
/// (cardID/cardId, phone provider/network, and the ptotoType-ptotoData/
/// dataType-data biometric blob keys all differed from the guess); those
/// have since been corrected against the real payload.
/// </summary>
public static class VerificationResponseMasker
{
    /// <summary>
    /// The client-facing stand-in for whatever userID value the real upstream
    /// echoes back (e.g. "XDS_NIA") — that raw value names the vendor
    /// directly, defeating the whole point of masking. Always substituted,
    /// never passed through, on both the success and failure paths.
    /// </summary>
    private const string MaskedUserId = "XDS_Ver";

    public static JsonObject MaskKycResponse(JsonNode? niaResponse, List<AddressHistoryEntry> addressHistory)
    {
        var data = niaResponse?["data"];
        var person = data?["person"];

        var maskedData = new JsonObject();
        // CONFIRMED — seen on real live NIA responses (both success and failure paths).
        SetIfPresent(maskedData, "Tranx_NID", data?["transactionGuid"]);
        SetIfPresent(maskedData, "ShortXGUID", data?["shortGuid"]);
        SetIfPresent(maskedData, "requestTimestamp", data?["requestTimestamp"]);
        SetIfPresent(maskedData, "responseTimestamp", data?["responseTimestamp"]);
        SetIfPresent(maskedData, "N_verified", data?["verified"]);
        if (data?["userID"] is not null)
        {
            maskedData["N_userID"] = MaskedUserId;
        }
        SetIfPresent(maskedData, "N_center", data?["center"]);
        // merchantName/merchantCode/source/modeOfOperation/badFingerPosition/isException/onWatchList
        // are deliberately dropped — vendor-internal fields, not part of the masked contract.
        maskedData["person"] = MaskPerson(person, addressHistory);

        var result = new JsonObject { ["data"] = maskedData };
        // CONFIRMED
        SetIfPresent(result, "N_success", niaResponse?["success"]);
        SetIfPresent(result, "N_StatusCode", niaResponse?["code"]);
        SetIfPresent(result, "N_CodeMessage", niaResponse?["msg"]);
        return result;
    }

    private static JsonObject MaskPerson(JsonNode? person, List<AddressHistoryEntry> addressHistory)
    {
        var masked = new JsonObject();
        // CONFIRMED — the only person field seen on a real (failure-path) live response.
        SetIfPresent(masked, "IDNo", person?["nationalId"]);

        // CONFIRMED — real live successful-match response.
        SetIfPresent(masked, "N_cardID", person?["cardId"]);
        SetIfPresent(masked, "N_cardValidFrom", person?["cardValidFrom"]);
        SetIfPresent(masked, "N_cardValidTo", person?["cardValidTo"]);
        SetIfPresent(masked, "N_surname", person?["surname"]);
        SetIfPresent(masked, "N_forenames", person?["forenames"]);
        SetIfPresent(masked, "N_nationality", person?["nationality"]);
        SetIfPresent(masked, "N_birthDate", person?["birthDate"]);
        SetIfPresent(masked, "N_gender", person?["gender"]);
        SetIfPresent(masked, "N_birthCountry", person?["birthCountry"]);
        SetIfPresent(masked, "N_birthDistrict", person?["birthDistrict"]);
        SetIfPresent(masked, "N_birthRegion", person?["birthRegion"]);
        SetIfPresent(masked, "N_birthTown", person?["birthTown"]);

        if (person?["addresses"] is JsonArray addresses)
        {
            masked["addresses"] = MaskAddresses(addresses);
        }

        // CONFIRMED (credit API side) — placement inside `person`, alongside `addresses`, per spec.
        masked["X_addressHistory"] = BuildAddressHistory(addressHistory);

        if (person?["contact"] is { } contact)
        {
            masked["contact"] = MaskContact(contact);
        }
        if (person?["occupations"] is JsonArray occupations)
        {
            masked["occupations"] = MaskOccupations(occupations);
        }
        if (person?["biometricFeed"] is { } biometricFeed)
        {
            masked["biometricFeed"] = MaskBiometricFeed(biometricFeed);
        }
        if (person?["binaries"] is JsonArray binaries)
        {
            masked["binaries"] = MaskBinaries(binaries);
        }

        return masked;
    }

    private static JsonArray MaskAddresses(JsonArray addresses)
    {
        var result = new JsonArray();
        foreach (var addr in addresses)
        {
            if (addr is null) continue;
            var masked = new JsonObject();
            SetIfPresent(masked, "N_type", addr["type"]);
            SetIfPresent(masked, "N_town", addr["town"]);
            SetIfPresent(masked, "N_community", addr["community"]);
            SetIfPresent(masked, "N_postalCode", addr["postalCode"]);
            SetIfPresent(masked, "N_countryName", addr["countryName"]);
            SetIfPresent(masked, "N_districtName", addr["districtName"]);
            SetIfPresent(masked, "N_region", addr["region"]);
            SetIfPresent(masked, "N_addressDigital", addr["addressDigital"]);

            if (addr["gpsAddressDetails"] is { } gps)
            {
                var maskedGps = new JsonObject();
                SetIfPresent(maskedGps, "N_gpsName", gps["gpsName"]);
                SetIfPresent(maskedGps, "N_region", gps["region"]);
                SetIfPresent(maskedGps, "N_district", gps["district"]);
                SetIfPresent(maskedGps, "N_area", gps["area"]);
                SetIfPresent(maskedGps, "N_street", gps["street"]);
                SetIfPresent(maskedGps, "N_longitude", gps["longitude"]);
                SetIfPresent(maskedGps, "N_latitude", gps["latitude"]);
                masked["gpsAddressDetails"] = maskedGps;
            }

            result.Add(masked);
        }
        return result;
    }

    private static JsonObject MaskContact(JsonNode contact)
    {
        var masked = new JsonObject();
        SetIfPresent(masked, "N_email", contact["email"]);

        if (contact["phoneNumbers"] is JsonArray phones)
        {
            var maskedPhones = new JsonArray();
            foreach (var phone in phones)
            {
                if (phone is null) continue;
                var maskedPhone = new JsonObject();
                SetIfPresent(maskedPhone, "N_type", phone["type"]);
                SetIfPresent(maskedPhone, "N_phoneNumber", phone["phoneNumber"]);
                SetIfPresent(maskedPhone, "N_Provider", phone["network"]);
                maskedPhones.Add(maskedPhone);
            }
            masked["phoneNumbers"] = maskedPhones;
        }

        return masked;
    }

    private static JsonArray MaskOccupations(JsonArray occupations)
    {
        var result = new JsonArray();
        foreach (var occupation in occupations)
        {
            if (occupation is null) continue;
            var masked = new JsonObject();
            SetIfPresent(masked, "N_name", occupation["name"]);
            result.Add(masked);
        }
        return result;
    }

    private static JsonObject MaskBiometricFeed(JsonNode biometricFeed)
    {
        var masked = new JsonObject();
        if (biometricFeed["face"] is { } face)
        {
            var maskedFace = new JsonObject();
            // CONFIRMED — real live response: {"face":{"dataType":"PNG","data":"<base64>"}}.
            SetIfPresent(maskedFace, "N_PtotoType", face["dataType"]);
            SetIfPresent(maskedFace, "N_PtotoData", face["data"]);
            masked["face"] = maskedFace;
        }
        return masked;
    }

    private static JsonArray MaskBinaries(JsonArray binaries)
    {
        var result = new JsonArray();
        foreach (var binary in binaries)
        {
            if (binary is null) continue;
            var masked = new JsonObject();
            // CONFIRMED — real live response: {"type":"SIGNATURE","dataType":"JPEG","data":"<base64>"}.
            SetIfPresent(masked, "N_type", binary["type"]);
            SetIfPresent(masked, "N_PtotoType", binary["dataType"]);
            SetIfPresent(masked, "N_PtotoData", binary["data"]);
            result.Add(masked);
        }
        return result;
    }

    /// <summary>CONFIRMED — real field names from live GetConsumerFullCreditReport testing.</summary>
    private static JsonArray BuildAddressHistory(List<AddressHistoryEntry> entries)
    {
        var result = new JsonArray();
        foreach (var entry in entries)
        {
            result.Add(new JsonObject
            {
                ["X_upDateDate"] = entry.UpDateDate,
                ["X_upDateOnDate"] = entry.UpDateOnDate,
                ["X_address1"] = entry.Address1,
                ["X_address2"] = entry.Address2,
                ["X_address3"] = entry.Address3,
                ["X_address4"] = entry.Address4,
                ["X_addressTypeInd"] = entry.AddressTypeInd,
            });
        }
        return result;
    }

    private static void SetIfPresent(JsonObject target, string key, JsonNode? value)
    {
        if (value is not null)
        {
            target[key] = value.DeepClone();
        }
    }

    /// <summary>
    /// Masks a KYC failure response — unlike the success path, there's no
    /// N_/X_ split to make (no credit lookup ever runs on a failure), so
    /// every key is uniformly prefixed X_ instead of left as raw NIA field
    /// names. userID gets the same MaskedUserId substitution as the success path.
    /// </summary>
    public static JsonNode? MaskKycFailureResponse(JsonNode? rawErrorDetail) => PrefixAllKeys(rawErrorDetail);

    private static JsonNode? PrefixAllKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    var prefixedKey = "X_" + key;
                    result[prefixedKey] = string.Equals(key, "userID", StringComparison.OrdinalIgnoreCase)
                        ? MaskedUserId
                        : PrefixAllKeys(value?.DeepClone());
                }
                return result;

            case JsonArray arr:
                var arrResult = new JsonArray();
                foreach (var item in arr)
                {
                    arrResult.Add(PrefixAllKeys(item?.DeepClone()));
                }
                return arrResult;

            default:
                return node;
        }
    }
}
