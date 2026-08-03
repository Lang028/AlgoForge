using AlgoForge.Models;
using System.Text.RegularExpressions;

namespace AlgoForge.Services
{
    public class AttendeeImportResult
    {
        public List<Attendee> ValidAttendees { get; set; } = new();
        public List<AttendeeImportError> Errors { get; set; } = new();
    }

    public class AttendeeImportError
    {
        public int RowNumber { get; set; }
        public string Field { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class AttendeeImportService
    {
        // Basic email regex: something@something.something
        private static readonly Regex EmailRegex =
            new Regex(@"^[^@]+@[^@]+\.[^@]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Exposed so the "import the valid rows anyway" path can hold rows to the same bar
        // as the parse did. That path receives its rows back through hidden form fields,
        // which means what it is handed is whatever was posted, not what was parsed.
        public static bool IsValidEmail(string? email) =>
            !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email.Trim());

        public static bool IsValidName(string? name) =>
            !string.IsNullOrWhiteSpace(name);

        /// <summary>
        /// Parses a CSV stream and returns valid Attendee objects and row-level errors.
        /// Does NOT write to the database.
        /// </summary>
        public AttendeeImportResult ParseCsv(Stream csvStream, Guid eventId)
        {
            var result = new AttendeeImportResult();

            // detectEncodingFromByteOrderMarks=true handles UTF-8 BOM automatically
            using var reader = new StreamReader(csvStream, detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);

            string? headerLine = reader.ReadLine();
            if (headerLine is null)
                return result; // empty file

            // Strip BOM character if still present after StreamReader BOM detection
            headerLine = headerLine.TrimStart('\uFEFF');

            var headers = ParseCsvLine(headerLine);
            int nameIdx = FindColumnIndex(headers, "Name");
            int emailIdx = FindColumnIndex(headers, "Email");
            int contactIdx = FindColumnIndex(headers, "ContactInfo");

            if (nameIdx < 0 || emailIdx < 0 || contactIdx < 0)
            {
                result.Errors.Add(new AttendeeImportError
                {
                    RowNumber = 1,
                    Field = "Header",
                    Reason = "CSV must contain columns: Name, Email, ContactInfo"
                });
                return result;
            }

            // dataRowNumber matches spreadsheet row numbers (header = row 1, first data = row 2)
            int dataRowNumber = 1;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                dataRowNumber++;

                // Skip blank lines silently per spec
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = ParseCsvLine(line);
                int maxIdx = Math.Max(nameIdx, Math.Max(emailIdx, contactIdx));
                if (fields.Count <= maxIdx)
                {
                    result.Errors.Add(new AttendeeImportError
                    {
                        RowNumber = dataRowNumber,
                        Field = "Row",
                        Reason = "Row has fewer columns than expected"
                    });
                    continue;
                }

                string name = fields[nameIdx].Trim();
                string email = fields[emailIdx].Trim();
                string contactInfo = fields[contactIdx].Trim();

                bool rowValid = true;

                if (string.IsNullOrEmpty(name))
                {
                    result.Errors.Add(new AttendeeImportError
                    {
                        RowNumber = dataRowNumber,
                        Field = "Name",
                        Reason = "Name is empty"
                    });
                    rowValid = false;
                }

                if (string.IsNullOrEmpty(email))
                {
                    result.Errors.Add(new AttendeeImportError
                    {
                        RowNumber = dataRowNumber,
                        Field = "Email",
                        Reason = "Email is empty"
                    });
                    rowValid = false;
                }
                else if (!EmailRegex.IsMatch(email))
                {
                    result.Errors.Add(new AttendeeImportError
                    {
                        RowNumber = dataRowNumber,
                        Field = "Email",
                        Reason = "Email is invalid"
                    });
                    rowValid = false;
                }

                if (string.IsNullOrEmpty(contactInfo))
                {
                    result.Errors.Add(new AttendeeImportError
                    {
                        RowNumber = dataRowNumber,
                        Field = "ContactInfo",
                        Reason = "ContactInfo is empty"
                    });
                    rowValid = false;
                }

                if (rowValid)
                {
                    result.ValidAttendees.Add(new Attendee
                    {
                        Id = Guid.NewGuid(),
                        EventId = eventId,
                        Name = name,
                        Email = email,
                        ContactInfo = contactInfo,
                        InviteToken = string.Empty
                    });
                }
            }

            return result;
        }

        // Naive RFC-4180-compatible field splitter.
        // Handles quoted fields and escaped double-quotes ("").
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }

        private static int FindColumnIndex(List<string> headers, string name)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}
