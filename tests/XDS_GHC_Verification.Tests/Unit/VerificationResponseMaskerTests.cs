using System.Text.Json.Nodes;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Unit;

public class VerificationResponseMaskerTests
{
    // Shape transcribed from a real, live NIA failure response captured during
    // integration testing — the only fields CONFIRMED against a real call.
    private static JsonNode RealConfirmedNiaResponse => JsonNode.Parse("""
        {
          "data": {
            "transactionGuid": "a490450c307b437dacbf6b3ad8e890d9",
            "shortGuid": null,
            "requestTimestamp": "2026-08-22T09:54:18.250Z",
            "responseTimestamp": "2026-08-22T09:54:18.287Z",
            "verified": "false",
            "userID": "XDS_NIA",
            "center": "BRANCHLESS",
            "merchantName": "XDS DATA",
            "person": { "nationalId": "GHA-000000000-0" }
          },
          "success": false,
          "code": "11",
          "msg": "Failed to detect face, please retake picture, ensure enough lighting"
        }
        """)!;

    private static List<AddressHistoryEntry> RealConfirmedAddressHistory =>
    [
        new AddressHistoryEntry
        {
            UpDateDate = "19/06/2023",
            UpDateOnDate = "19/06/2023",
            Address1 = "ATIMATIM",
            Address2 = "",
            Address3 = "",
            Address4 = "",
            AddressTypeInd = "Residential",
        },
    ];

    [Fact]
    public void MaskKycResponse_RenamesConfirmedTopLevelFields()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedNiaResponse, [AddressHistoryEntry.Empty]);

        Assert.Equal("a490450c307b437dacbf6b3ad8e890d9", masked["data"]!["Tranx_NID"]!.GetValue<string>());
        Assert.Equal("false", masked["data"]!["N_verified"]!.GetValue<string>());
        Assert.Equal("BRANCHLESS", masked["data"]!["N_center"]!.GetValue<string>());
        Assert.False(masked["N_success"]!.GetValue<bool>());
        Assert.Equal("11", masked["N_StatusCode"]!.GetValue<string>());
        Assert.Equal("Failed to detect face, please retake picture, ensure enough lighting", masked["N_CodeMessage"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycResponse_DropsVendorInternalFields()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedNiaResponse, [AddressHistoryEntry.Empty]);

        var dataObj = masked["data"]!.AsObject();
        Assert.False(dataObj.ContainsKey("merchantName"));
        Assert.False(dataObj.ContainsKey("merchantCode"));
        Assert.False(dataObj.ContainsKey("source"));
        Assert.False(dataObj.ContainsKey("userID")); // renamed to N_userID, not left in place
    }

    [Fact]
    public void MaskKycResponse_MapsNationalIdToIDNo()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedNiaResponse, [AddressHistoryEntry.Empty]);

        Assert.Equal("GHA-000000000-0", masked["data"]!["person"]!["IDNo"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycResponse_EmbedsAddressHistoryUnderXPrefix()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedNiaResponse, RealConfirmedAddressHistory);

        var history = masked["data"]!["person"]!["X_addressHistory"]!.AsArray();
        var entry = history[0]!;
        Assert.Equal("ATIMATIM", entry["X_address1"]!.GetValue<string>());
        Assert.Equal("Residential", entry["X_addressTypeInd"]!.GetValue<string>());
        Assert.Equal("19/06/2023", entry["X_upDateDate"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycResponse_NoMatchPlaceholder_HasAllNullValues()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedNiaResponse, [AddressHistoryEntry.Empty]);

        var history = masked["data"]!["person"]!["X_addressHistory"]!.AsArray();
        var entry = Assert.Single(history)!;
        Assert.Null(entry["X_upDateDate"]);
        Assert.Null(entry["X_address1"]);
        Assert.Null(entry["X_addressTypeInd"]);
    }

    [Fact]
    public void MaskKycResponse_NullInput_DoesNotThrow()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(null, [AddressHistoryEntry.Empty]);

        Assert.NotNull(masked);
        Assert.NotNull(masked["data"]);
    }

    [Fact]
    public void MaskKycResponse_UserIdIsAlwaysSubstitutedNeverPassedThrough()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedNiaResponse, [AddressHistoryEntry.Empty]);

        Assert.Equal("XDS_Ver", masked["data"]!["N_userID"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycFailureResponse_PrefixesEveryKeyWithX()
    {
        var masked = VerificationResponseMasker.MaskKycFailureResponse(RealConfirmedNiaResponse);

        Assert.NotNull(masked!["X_data"]);
        Assert.Equal("a490450c307b437dacbf6b3ad8e890d9", masked["X_data"]!["X_transactionGuid"]!.GetValue<string>());
        Assert.Equal("false", masked["X_data"]!["X_verified"]!.GetValue<string>());
        Assert.Equal("BRANCHLESS", masked["X_data"]!["X_center"]!.GetValue<string>());
        Assert.Equal("GHA-000000000-0", masked["X_data"]!["X_person"]!["X_nationalId"]!.GetValue<string>());
        Assert.False(masked["X_success"]!.GetValue<bool>());
        Assert.Equal("11", masked["X_code"]!.GetValue<string>());
        // No N_ prefixes anywhere on the failure path — no credit lookup ever ran to justify the split.
        Assert.False(masked.AsObject().ContainsKey("N_success"));
    }

    [Fact]
    public void MaskKycFailureResponse_UserIdIsAlsoSubstituted()
    {
        var masked = VerificationResponseMasker.MaskKycFailureResponse(RealConfirmedNiaResponse);

        Assert.Equal("XDS_Ver", masked!["X_data"]!["X_userID"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycFailureResponse_NullInput_DoesNotThrow()
    {
        var masked = VerificationResponseMasker.MaskKycFailureResponse(null);

        Assert.Null(masked);
    }

    // Shape transcribed from a real, live NIA *successful* KYC match captured
    // during integration testing (values replaced with synthetic data) — this
    // is what caught cardId/cardID, phone network/provider, and the biometric
    // dataType+data/ptotoType+ptotoData key mismatches, none of which had ever
    // been exercised against a real successful match before.
    private static JsonNode RealConfirmedSuccessfulMatchResponse => JsonNode.Parse("""
        {
          "data": {
            "person": {
              "nationalId": "GHA-999999999-9",
              "cardId": "GH0000000",
              "cardValidFrom": "2019-04-10",
              "cardValidTo": "2029-04-09",
              "surname": "DOE",
              "forenames": "JANE",
              "nationality": "Ghana",
              "birthDate": "1990-01-01",
              "gender": "FEMALE",
              "birthCountry": "Ghana",
              "birthDistrict": "SAMPLE DISTRICT",
              "birthRegion": "SAMPLE REGION",
              "birthTown": "SAMPLE TOWN",
              "contact": {
                "email": "jane.doe@example.com",
                "phoneNumbers": [
                  { "type": "MOBILE", "phoneNumber": "0200000000", "network": "MTN" }
                ]
              },
              "occupations": [ { "name": "Software Developer" } ],
              "biometricFeed": {
                "face": { "dataType": "PNG", "data": "face-base64-placeholder" }
              },
              "binaries": [
                { "type": "SIGNATURE", "dataType": "JPEG", "data": "signature-base64-placeholder" }
              ]
            }
          },
          "success": true,
          "code": "00",
          "msg": "Verified Successfully"
        }
        """)!;

    [Fact]
    public void MaskKycResponse_MapsCardIdFromRawLowercaseKey()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedSuccessfulMatchResponse, [AddressHistoryEntry.Empty]);

        Assert.Equal("GH0000000", masked["data"]!["person"]!["N_cardID"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycResponse_MapsPhoneProviderFromRawNetworkKey()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedSuccessfulMatchResponse, [AddressHistoryEntry.Empty]);

        var phone = masked["data"]!["person"]!["contact"]!["phoneNumbers"]![0]!;
        Assert.Equal("MTN", phone["N_Provider"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycResponse_MapsFacePhotoFromRawDataTypeAndDataKeys()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedSuccessfulMatchResponse, [AddressHistoryEntry.Empty]);

        var face = masked["data"]!["person"]!["biometricFeed"]!["face"]!;
        Assert.Equal("PNG", face["N_PtotoType"]!.GetValue<string>());
        Assert.Equal("face-base64-placeholder", face["N_PtotoData"]!.GetValue<string>());
    }

    [Fact]
    public void MaskKycResponse_MapsBinaryPhotoFromRawDataTypeAndDataKeys()
    {
        var masked = VerificationResponseMasker.MaskKycResponse(RealConfirmedSuccessfulMatchResponse, [AddressHistoryEntry.Empty]);

        var signature = masked["data"]!["person"]!["binaries"]![0]!;
        Assert.Equal("SIGNATURE", signature["N_type"]!.GetValue<string>());
        Assert.Equal("JPEG", signature["N_PtotoType"]!.GetValue<string>());
        Assert.Equal("signature-base64-placeholder", signature["N_PtotoData"]!.GetValue<string>());
    }
}
