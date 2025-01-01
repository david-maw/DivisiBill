using DivisiBill.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DivisiBill.Models;

/// <summary>
/// Stores the content that comes back from OCR of a bill, used for debugging mostly
/// </summary>
public class ScannedBill
{
    public ScannedBill()
    {
    }
    #region Properties
    public string SourceName { get; set; }

    /// <summary>
    /// Number of scans remaining on the license used
    /// </summary>
    public int ScansLeft { get; set; } = default;

    public List<OrderLine> OrderLines { get; set; } = new List<OrderLine>();

    public List<FormElement> FormElements { get; set; } = new List<FormElement>();
    #endregion

    #region Serialization 
    /// <summary>
    /// Handle serialization and deserialization of scan results so debugging need not require round trips to the scanner
    /// </summary>
    private static XmlSerializer itemsSerializer = new XmlSerializer(typeof(ScannedBill));

    private void Serialize(Stream s)
    {
        using (StreamWriter sw = new StreamWriter(s, Encoding.UTF8, 512, true))
        using (var xmlwriter = XmlWriter.Create(sw, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true, NewLineOnAttributes = true }))
        {
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add(string.Empty, string.Empty);
            itemsSerializer.Serialize(xmlwriter, this, namespaces);
        }
    }

    /// <summary>
    /// Stores the current ScannedBill info to a file so it can be read later for debug purposes
    /// </summary>
    public void StoreToFile()
    {
        if (SourceName is null)
            SourceName = Meal.CurrentMeal.FileName;
        string TargetFilePath = Path.Combine(Meal.ImageFolderPath, Path.ChangeExtension(SourceName, "xml"));
        using (var stream = File.Open(TargetFilePath, FileMode.Create)) // Overwrites any existing file
        {
            Serialize(stream);
            Utilities.DebugExamineStream(stream);
        }
    }
    private static ScannedBill Deserialize(Stream s)
    {
        using (StreamReader sr = new StreamReader(s, Encoding.UTF8, true, 512, true))
        using (var xmlreader = XmlReader.Create(sr))
        {
            return (ScannedBill)itemsSerializer.Deserialize(xmlreader);
        }
    }

    public static ScannedBill LoadFromFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        ScannedBill result = null;
        string TargetFilePath = Path.Combine(Meal.ImageFolderPath, Path.ChangeExtension(fileName, "xml"));
        if (!File.Exists(TargetFilePath))
        {
            result = new ScannedBill();
        }
        else
        {
            using (var stream = File.Open(TargetFilePath, FileMode.Open)) // Expect an existing file
            {
                result = Deserialize(stream);
                if (Debugger.IsAttached)
                {
                    stream.Position = 0;
                    StreamReader sr = new StreamReader(stream);
                    string myString = sr.ReadToEnd(); // Allows the encoded version to be viewed in the debugger
                }
            }
        }
        result.SourceName = fileName;
        return result;
    }
    #endregion


    /// <summary>
    /// Convert the scanned information from a ScannedBill object to a list of LineItem objects for insertion in a Meal.
    /// </summary>
    /// <returns>List of LineItem objects derived from the ScannedBill object</returns>
    public List<LineItem> ToLineItems()
    {
        List<LineItem> lineItems = new List<LineItem>();

        // Handle the situation where the description column and the cost column are off by one so the 
        // first or last line has a description with no cost and the last or first line has a cost with no description
        // This occasionally happens with AWS Textract
        int lastLineInx = OrderLines.Count - 1;
        if (lastLineInx > 0) // Two or more lines
        {
            OrderLine priorItem = null;
            if (string.IsNullOrWhiteSpace(OrderLines[0].ItemName) && string.IsNullOrWhiteSpace(OrderLines[lastLineInx].ItemCost))
            { // The name column is offset below the cost column
                OrderLines.ForEach(orderLine =>
                {
                    if (priorItem is not null)
                        priorItem.ItemName = orderLine.ItemName;
                    priorItem = orderLine;
                });
                OrderLines.RemoveAt(lastLineInx);
            }
            else if (string.IsNullOrWhiteSpace(OrderLines[0].ItemCost) && string.IsNullOrWhiteSpace(OrderLines[lastLineInx].ItemName))
            { // The cost column is offset below the name column
                OrderLines.ForEach(orderLine =>
                {
                    if (priorItem is not null)
                        priorItem.ItemCost = orderLine.ItemCost;
                    priorItem = orderLine;
                });
                OrderLines.RemoveAt(lastLineInx);
            }
        }
        OrderLines.ForEach(orderLine => lineItems.Add(orderLine.ToLineItem()));
        return lineItems;
    }
}

/// <summary>
/// Individual lines in each order
/// </summary>
public class OrderLine
{
    public string ItemName { get; set; }
    public string ItemCost { get; set; }

    /// <summary>
    /// Convert an OrderLine to a LineItem handling two special cases 
    /// Individual descriptions are limited to a single line so replace newline with space (Azure FormRecognizer occasionally creates these)
    /// Stray characters from the end of the description can end up on the front of the amount, if so, put them back (AWS Textract occasionally creates these)
    /// </summary>
    /// <returns></returns>
    public LineItem ToLineItem()
    {
        (decimal amount, string leadingText) = CurrencyStringToAmount(ItemCost);
        if (ItemName.Contains('\n')) // Happens with Azure FormRecognizer occasionally
        {
            // Just forget about any possible stray text because we've no good idea where to put it
            return new LineItem()
            {
                ItemName = ItemName.Replace('\n', ' '),
                Amount = amount
            };
        }
        else
        {
            // Append any stray text to the end of the item description
            return new LineItem()
            {
                ItemName = ItemName + leadingText,
                Amount = amount
            };
        }
    }

    /// <summary>
    /// Extract a decimal amount from a currency string. For now it handles just simple cases of dollar
    /// amounts (like "$12.34") or simple numbers like "1.23" or "12"  even if they are surrounded by other text.
    /// There are, of course arbitrarily more complex cases especially in other currencies, but we're not 
    /// going there for now, or even handling thousands separators (for example "$1,234.56" in dollar amounts).
    /// 
    /// The basic algorithm is to back up from the end of the string until you find a decimal separator followed
    /// by an appropriate number of digits or, failing that a single digit.
    /// 
    /// Then back up from there one character at a time until what's there is not a number - the last valid number 
    /// is what is returned.
    /// </summary>
    /// <param name="currencyText">A string representing an amount, for example $12.34" or "1.23"</param>
    /// <returns>A decimal representation of the amount and any leading text</returns>
    private static (decimal amount, string leadingtext) CurrencyStringToAmount(string currencyText)
    {
        if (string.IsNullOrEmpty(currencyText))
            return (0, string.Empty);
        NumberFormatInfo nfi = NumberFormatInfo.CurrentInfo;
        int currencyTextLen = currencyText.Length; // Total number field length
        int startInx = currencyTextLen - nfi.NumberDecimalDigits; // Working index of the first valid character in the number
        int endInx = -1; // the index of the last digit in the number

        while (startInx > 0 && ((startInx = currencyText.LastIndexOf(nfi.CurrencyDecimalSeparator, startInx - 1)) > 0))
        {
            // We found a decimal separator at startInx
            if (currencyTextLen - startInx - 1 >= nfi.NumberDecimalDigits)
            {
                int i;
                //There may be digits
                for (i = 0; i < nfi.NumberDecimalDigits; i++)
                {
                    if (!char.IsDigit(currencyText[startInx + 1 + i]))
                        continue;
                }
                endInx = startInx + i;
                break; // because if we got this far we have a decimal separator followed by enough digits
            }
        }
        if (endInx < 0) // we did not find a decimal separator followed by NumberDecimalDigits 
        {
            // Try for an integral number instead
            for (startInx = currencyTextLen - 1; startInx >= 0; startInx--)
            {
                if (char.IsDigit(currencyText[startInx]))
                {
                    endInx = startInx;
                    break;
                }
            }
        }

        // Now just keep parsing the number, backing up by 1 each time until it doesn't parse any more

        decimal result = 0;
        for (; startInx >= 0; startInx--)
        {
            if (decimal.TryParse(currencyText.Substring(startInx, endInx - startInx + 1), out decimal parsedNumber))
                result = parsedNumber;
            else
                break;
        }

        // Now figure out whether there is any leading text that ended up in the amount data, return it if there is

        string leadingText;
        if (endInx < 0)
            leadingText = currencyText;
        else if (startInx >= 0)
        {
            leadingText = currencyText.Substring(0, startInx + 1).Trim();
            if (leadingText.EndsWith(nfi.CurrencySymbol)) // discard any trailing currency symbol
            {
                leadingText = leadingText.Substring(0, leadingText.Length - nfi.CurrencySymbol.Length).TrimEnd();
            }
        }
        else
            leadingText = string.Empty;
        return (result, leadingText);
    }
}
public class FormElement
{
    public string FieldName { get; set; }
    public string FieldValue { get; set; }

}
