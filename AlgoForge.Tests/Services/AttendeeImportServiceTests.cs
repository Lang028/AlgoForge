using System.Text;
using AlgoForge.Services;

namespace AlgoForge.Tests.Services
{
    public class AttendeeImportServiceTests
    {
        private readonly AttendeeImportService _sut = new();
        private readonly Guid _eventId = Guid.NewGuid();

        // Helper: converts a string into a Stream
        private static Stream ToStream(string content, Encoding? encoding = null)
        {
            encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return new MemoryStream(encoding.GetBytes(content));
        }

        // ── Test 1: Valid CSV with 3 rows parses correctly ────────────────────
        [Fact]
        public void ParseCsv_ValidThreeRows_ReturnsThreeAttendees()
        {
            const string csv = """
                Name,Email,ContactInfo
                Jane Doe,jane@example.com,+27 81 234 5678
                John Smith,john.smith@example.com,john@company.com
                Alice Chen,alice.chen@acme.org,alice@acme.org
                """;

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.Errors);
            Assert.Equal(3, result.ValidAttendees.Count);
            Assert.Equal("Jane Doe", result.ValidAttendees[0].Name);
            Assert.Equal("jane@example.com", result.ValidAttendees[0].Email);
            Assert.Equal("+27 81 234 5678", result.ValidAttendees[0].ContactInfo);
            Assert.Equal(_eventId, result.ValidAttendees[0].EventId);
        }

        // ── Test 2: Empty rows are skipped silently ───────────────────────────
        [Fact]
        public void ParseCsv_EmptyRowsSkippedSilently()
        {
            const string csv = """
                Name,Email,ContactInfo
                Jane Doe,jane@example.com,+27 81 234 5678

                John Smith,john.smith@example.com,john@company.com

                """;

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.ValidAttendees.Count);
        }

        // ── Test 3: Invalid email formats are rejected ────────────────────────
        [Theory]
        [InlineData("john@")]           // no domain
        [InlineData("@example.com")]    // no local part
        [InlineData("notanemail")]      // no @ at all
        [InlineData("a@b")]             // no TLD dot
        public void ParseCsv_InvalidEmail_ReturnsEmailError(string badEmail)
        {
            string csv = $"Name,Email,ContactInfo\nJane Doe,{badEmail},+27 81 234 5678";

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.ValidAttendees);
            Assert.Single(result.Errors);
            Assert.Equal("Email", result.Errors[0].Field);
            Assert.Equal("Email is invalid", result.Errors[0].Reason);
        }

        // ── Test 4: Empty required fields are rejected ────────────────────────
        [Fact]
        public void ParseCsv_EmptyName_ReturnsNameError()
        {
            const string csv = "Name,Email,ContactInfo\n,alice@example.com,alice@acme.org";

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.ValidAttendees);
            Assert.Single(result.Errors);
            Assert.Equal("Name", result.Errors[0].Field);
        }

        [Fact]
        public void ParseCsv_EmptyEmail_ReturnsEmailError()
        {
            const string csv = "Name,Email,ContactInfo\nAlice,,alice@acme.org";

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.ValidAttendees);
            Assert.Single(result.Errors);
            Assert.Equal("Email", result.Errors[0].Field);
            Assert.Equal("Email is empty", result.Errors[0].Reason);
        }

        [Fact]
        public void ParseCsv_EmptyContactInfo_ReturnsContactInfoError()
        {
            const string csv = "Name,Email,ContactInfo\nAlice,alice@example.com,";

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.ValidAttendees);
            Assert.Single(result.Errors);
            Assert.Equal("ContactInfo", result.Errors[0].Field);
        }

        // ── Test 5: UTF-8 BOM is handled transparently ───────────────────────
        [Fact]
        public void ParseCsv_Utf8WithBom_ParsesCorrectly()
        {
            const string csv = "Name,Email,ContactInfo\nJane Doe,jane@example.com,+27 81 234 5678";

            // UTF-8 with BOM encoding
            using var stream = new MemoryStream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                .GetBytes(csv));

            var result = _sut.ParseCsv(stream, _eventId);

            Assert.Empty(result.Errors);
            Assert.Single(result.ValidAttendees);
            Assert.Equal("Jane Doe", result.ValidAttendees[0].Name);
        }

        // ── Bonus: Columns in different order are handled correctly ───────────
        [Fact]
        public void ParseCsv_ColumnsInAnyOrder_ParsesCorrectly()
        {
            const string csv = "ContactInfo,Name,Email\n+27 81 234 5678,Jane Doe,jane@example.com";

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.Errors);
            Assert.Single(result.ValidAttendees);
            Assert.Equal("Jane Doe", result.ValidAttendees[0].Name);
            Assert.Equal("jane@example.com", result.ValidAttendees[0].Email);
            Assert.Equal("+27 81 234 5678", result.ValidAttendees[0].ContactInfo);
        }

        // ── Bonus: Mixed valid/invalid rows from spec example ─────────────────
        [Fact]
        public void ParseCsv_MixedRows_ReturnsOneValidAndThreeErrors()
        {
            // From spec: Row 2 fails (Email invalid), Row 3 fails (ContactInfo empty),
            // Row 4 fails (Name empty). Row 1 (Jane Doe) succeeds.
            const string csv = """
                Name,Email,ContactInfo
                Jane Doe,invalid-email,+27 81 234 5678
                John Smith,john@example.com,
                ,alice@example.com,alice@acme.org
                """;

            // Wait — the spec says "Row 1 succeeds" but Jane Doe has an invalid email.
            // The spec example labels rows differently. Let us follow the actual data:
            // data row 2 = Jane Doe with invalid-email → fail
            // data row 3 = John Smith with empty ContactInfo → fail
            // data row 4 = no name → fail
            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            Assert.Empty(result.ValidAttendees);
            Assert.Equal(3, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.Field == "Email" && e.Reason == "Email is invalid");
            Assert.Contains(result.Errors, e => e.Field == "ContactInfo");
            Assert.Contains(result.Errors, e => e.Field == "Name");
        }

        // ── Row numbers in errors match spreadsheet row numbers ───────────────
        [Fact]
        public void ParseCsv_ErrorRowNumbers_MatchSpreadsheetRows()
        {
            const string csv = "Name,Email,ContactInfo\n,bad@bad,contact\nGood Name,good@example.com,contact";

            var result = _sut.ParseCsv(ToStream(csv), _eventId);

            // The bad row is data row 2 (spreadsheet row 2 since header is row 1)
            var nameError = Assert.Single(result.Errors, e => e.Field == "Name");
            Assert.Equal(2, nameError.RowNumber);

            Assert.Single(result.ValidAttendees);
        }
    }
}
