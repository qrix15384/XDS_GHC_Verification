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
        Assert.Equal("XDS_NIA", masked["data"]!["N_userID"]!.GetValue<string>());
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
}
