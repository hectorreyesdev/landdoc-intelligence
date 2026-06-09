using System.Text.Json;
using LandDoc.Api.Model;

namespace LandDoc.Tests;

/// <summary>
/// Unit tests for <see cref="AzureOpenAIChatClient.ParseFields(string)"/> — the static JSON-to-field
/// flattener (ADR-0015). All tests are fully offline; no Azure call is made. Covers three document
/// shapes: OGL (Lessor/Lessee + lease economics), Deed (Grantor/Grantee, no lease economics), and
/// AFE (Operator + otherNotableTerms, no lease economics).
/// </summary>
public sealed class AzureOpenAIChatClientParseFieldsTests
{
    private const string OglJson = """
        {
          "documentType": "Oil and Gas Lease",
          "parties": [
            {"role": "Lessor", "name": "Margaret A. Caldwell"},
            {"role": "Lessee", "name": "Llano Estacado Operating, LLC"}
          ],
          "effectiveDate": "January 15, 2025",
          "expirationDate": "January 15, 2030",
          "legalDescription": "Section 14, Block 2, T-1-N, Midland County, Texas",
          "county": "Midland",
          "state": "Texas",
          "acres": "160",
          "royalty": "one-fourth (1/4)",
          "bonus": "$500 per acre",
          "primaryTerm": "5 years",
          "otherNotableTerms": []
        }
        """;

    private const string DeedJson = """
        {
          "documentType": "Mineral Deed",
          "parties": [
            {"role": "Grantor", "name": "Dorothy M. Albright"},
            {"role": "Grantee", "name": "Chisholm Trail Royalties, LLC"}
          ],
          "effectiveDate": "March 10, 2025",
          "expirationDate": null,
          "legalDescription": "SW/4 of Section 22, Township 8 North, Range 6 West",
          "county": "Stephens",
          "state": "Oklahoma",
          "acres": "160",
          "royalty": null,
          "bonus": null,
          "primaryTerm": null,
          "otherNotableTerms": []
        }
        """;

    private const string AfeJson = """
        {
          "documentType": "Authority for Expenditure",
          "parties": [
            {"role": "Operator", "name": "Bakken Ridge Energy, Inc."}
          ],
          "effectiveDate": null,
          "expirationDate": null,
          "legalDescription": null,
          "county": "McKenzie",
          "state": "North Dakota",
          "acres": null,
          "royalty": null,
          "bonus": null,
          "primaryTerm": null,
          "otherNotableTerms": [
            {"name": "AFE Number", "value": "2025-001"},
            {"name": "Well Name", "value": "Bakken Unit 1H"},
            {"name": "Estimated Cost", "value": "$4,500,000"}
          ]
        }
        """;

    [Fact]
    public void ParseFields_OglJson_ReturnsDocumentTypeAndPartyFieldsAndEconomics()
    {
        var fields = AzureOpenAIChatClient.ParseFields(OglJson);

        Assert.Contains(fields, f => f is { Name: "DocumentType", Value: "Oil and Gas Lease" });
        Assert.Contains(fields, f => f is { Name: "Lessor", Value: "Margaret A. Caldwell" });
        Assert.Contains(fields, f => f is { Name: "Lessee", Value: "Llano Estacado Operating, LLC" });
        Assert.Contains(fields, f => f is { Name: "EffectiveDate", Value: "January 15, 2025" });
        Assert.Contains(fields, f => f is { Name: "ExpirationDate", Value: "January 15, 2030" });
        Assert.Contains(fields, f => f is { Name: "County", Value: "Midland" });
        Assert.Contains(fields, f => f is { Name: "State", Value: "Texas" });
        Assert.Contains(fields, f => f is { Name: "Acres", Value: "160" });
        Assert.Contains(fields, f => f is { Name: "Royalty", Value: "one-fourth (1/4)" });
        Assert.Contains(fields, f => f is { Name: "Bonus", Value: "$500 per acre" });
        Assert.Contains(fields, f => f is { Name: "PrimaryTerm", Value: "5 years" });
        Assert.All(fields, f => Assert.Null(f.SourceChunkId));
    }

    [Fact]
    public void ParseFields_DeedJson_HasGrantorGranteeAndAcres_NoLeaseEconomics()
    {
        var fields = AzureOpenAIChatClient.ParseFields(DeedJson);

        Assert.Contains(fields, f => f is { Name: "DocumentType", Value: "Mineral Deed" });
        Assert.Contains(fields, f => f is { Name: "Grantor", Value: "Dorothy M. Albright" });
        Assert.Contains(fields, f => f is { Name: "Grantee", Value: "Chisholm Trail Royalties, LLC" });
        Assert.Contains(fields, f => f is { Name: "Acres", Value: "160" });

        // Null lease economics must be omitted entirely
        Assert.DoesNotContain(fields, f => f.Name == "Royalty");
        Assert.DoesNotContain(fields, f => f.Name == "Bonus");
        Assert.DoesNotContain(fields, f => f.Name == "PrimaryTerm");
    }

    [Fact]
    public void ParseFields_AfeJson_HasOperatorAndOtherTerms_NoLeaseEconomicsOrNullScalars()
    {
        var fields = AzureOpenAIChatClient.ParseFields(AfeJson);

        Assert.Contains(fields, f => f is { Name: "DocumentType", Value: "Authority for Expenditure" });
        // Non-lease party role becomes the field Name
        Assert.Contains(fields, f => f is { Name: "Operator", Value: "Bakken Ridge Energy, Inc." });
        // Open escape-hatch terms
        Assert.Contains(fields, f => f is { Name: "AFE Number",     Value: "2025-001" });
        Assert.Contains(fields, f => f is { Name: "Well Name",      Value: "Bakken Unit 1H" });
        Assert.Contains(fields, f => f is { Name: "Estimated Cost", Value: "$4,500,000" });

        // Null scalars must be omitted
        Assert.DoesNotContain(fields, f => f.Name == "EffectiveDate");
        Assert.DoesNotContain(fields, f => f.Name == "ExpirationDate");
        Assert.DoesNotContain(fields, f => f.Name == "LegalDescription");
        Assert.DoesNotContain(fields, f => f.Name == "Acres");
        Assert.DoesNotContain(fields, f => f.Name == "Royalty");
        Assert.DoesNotContain(fields, f => f.Name == "Bonus");
        Assert.DoesNotContain(fields, f => f.Name == "PrimaryTerm");
    }

    [Fact]
    public void ParseFields_EmptyString_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => AzureOpenAIChatClient.ParseFields(""));
    }

    [Fact]
    public void ParseFields_WhitespaceString_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => AzureOpenAIChatClient.ParseFields("   "));
    }

    [Fact]
    public void ParseFields_MalformedJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => AzureOpenAIChatClient.ParseFields("not valid json"));
    }

    [Fact]
    public void ParseFields_AllFieldsHaveNullSourceChunkId()
    {
        var fields = AzureOpenAIChatClient.ParseFields(OglJson);
        Assert.All(fields, f => Assert.Null(f.SourceChunkId));
    }
}
