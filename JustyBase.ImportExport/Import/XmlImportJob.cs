using System.Data;
using System.Xml;

namespace JustyBase.ImportExport.Import;

/// <summary>Clipboard/XML import job contract (mirror of the Avalonia <c>IDbXMLImportJob</c>).</summary>
public interface IXmlImportJob : IImportJob
{
    Task AnalyzeXmlClipboardDataAndStoreLinesAsync(object someData, Action<string>? messageAction = null);
}

/// <summary>
/// Parses an Excel "XML Spreadsheet" clipboard payload into typed lines and feeds the
/// shared <see cref="ImportTypeAnalyzer"/> (port of the Avalonia <c>DbXMLImportJob</c>).
/// </summary>
public sealed class XmlImportJob : ImportJob, IXmlImportJob
{
    private OneCellValue[] _currentRow = [];
    private OneCellValue[][]? _linesX;
    private ImportTypeAnalyzer? _analyzer;
    private string[]? _headerNames;

    public async Task AnalyzeXmlClipboardDataAndStoreLinesAsync(object someData, Action<string>? messageAction = null)
    {
        XmlTextReader reader;
        IDisposable? toDispose = null;
        if (someData is byte[] xmlBytes)
        {
            MemoryStream ms = new(xmlBytes);
            reader = new XmlTextReader(ms);
            toDispose = reader;
        }
        else if (someData is XmlTextReader xmlTextReader)
        {
            reader = xmlTextReader;
        }
        else
        {
            return;
        }

        using (toDispose)
        {
            messageAction?.Invoke("clipboard analyze stared");
            await Task.Run(() =>
            {
                int actInd = -1;
                int cellNum = 0;
                int dataNum = 0;
                int colNum = 0;
                int rowNum = 0;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Whitespace)
                    {
                        continue;
                    }

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        int actRow = rowNum;
                        if (reader.Name == "Cell")
                        {
                            cellNum++;
                            if (reader.HasAttributes)
                            {
                                string? indS = reader.GetAttribute("ss:Index");
                                actInd = !string.IsNullOrEmpty(indS) ? int.Parse(indS) - 1 : -1; // xml has indexes from 1
                            }
                            else
                            {
                                actInd = -1;
                            }
                        }
                        else if (reader.Name == "Data")
                        {
                            dataNum++;
                            if (cellNum > dataNum)
                            {
                                colNum += cellNum - dataNum; // cell without data situation = <Cell />
                                cellNum = dataNum;
                            }

                            string? typeTxt = reader.GetAttribute("ss:Type");
                            string val = reader.ReadString();

                            if (rowNum == 0)
                            {
                                _currentRow[colNum] = new OneCellValue
                                {
                                    OriginalValue = val,
                                    TypePreferedValue = val
                                };
                                colNum++;
                            }
                            else
                            {
                                if (actInd != -1 && actRow == rowNum)
                                {
                                    colNum = actInd;
                                }

                                var ocv = new OneCellValue { OriginalValue = val };
                                _currentRow[colNum] = ocv;
                                if (typeTxt == "Boolean")
                                {
                                    ocv.OriginalValue = val == "0" ? "False" : "True";
                                    SetTypedValue(colNum, isBoolean: true);
                                }
                                else
                                {
                                    SetTypedValue(colNum);
                                }

                                colNum++;
                            }
                        }
                        else if (reader.Name == "Table")
                        {
                            for (int i = 0; i < reader.AttributeCount; i++)
                            {
                                reader.MoveToAttribute(i);
                                if (reader.Name == "ss:ExpandedColumnCount")
                                {
                                    int expandedColumnCount = int.Parse(reader.Value);
                                    _currentRow = new OneCellValue[expandedColumnCount];
                                    _analyzer = new ImportTypeAnalyzer(expandedColumnCount, inferBoolean: true);
                                    _headerNames = new string[expandedColumnCount];
                                }
                                else if (reader.Name == "ss:ExpandedRowCount")
                                {
                                    _linesX = new OneCellValue[int.Parse(reader.Value)][];
                                }
                            }
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "Row")
                    {
                        cellNum = 0;
                        dataNum = 0;
                        _linesX![rowNum++] = _currentRow;
                        _currentRow = new OneCellValue[_currentRow.Length];
                        colNum = 0;
                        if (rowNum == 1) // headers
                        {
                            _headerNames = _linesX[0]
                                .Select(arg => ImportNameHelper.NormalizeDbColumnName(arg?.OriginalValue ?? ImportNameHelper.RandomSuffix("COL_")))
                                .ToArray();
                            ImportNameHelper.DeDuplicate(_headerNames);
                        }

                        if (rowNum % 100_000 == 0 || rowNum == _linesX.Length - 1)
                        {
                            messageAction?.Invoke($"analyzed {rowNum:N0} rows");
                        }
                    }
                }
            });
        }

        if (_headerNames is null || _linesX is null || _analyzer is null)
        {
            throw new InvalidOperationException("Clipboard payload did not contain a table.");
        }

        IReadOnlyList<DetectedImportColumnType> detected = _analyzer.Choose(_headerNames);
        Columns = _headerNames.Select((name, i) => ToColumn(name, detected[i])).ToArray();
        ColumnHeadersNames = _headerNames;
        AsReader = new DataReaderFromLines(_linesX, Columns);
    }

    private void SetTypedValue(int columnNumber, bool isBoolean = false)
    {
        ImportColumnKind kind;
        string val;
        if (isBoolean)
        {
            kind = ImportColumnKind.Boolean;
            val = _currentRow[columnNumber].OriginalValue ?? string.Empty;
        }
        else
        {
            val = XmlCellClassifier.GetValueStringRepresentationWithType(
                out kind, _currentRow[columnNumber].OriginalValue ?? string.Empty,
                dataTypeAnnotation: false, textQualifier: "");
        }

        if (kind == ImportColumnKind.Integer
            && _currentRow[columnNumber].OriginalValue?.Trim().Length == 11
            && _headerNames![columnNumber].Contains("PESEL", StringComparison.OrdinalIgnoreCase))
        {
            kind = ImportColumnKind.Nvarchar;
            val = _currentRow[columnNumber].OriginalValue ?? string.Empty;
        }

        _currentRow[columnNumber].TypePreferedValue = val;
        _analyzer!.AddCell(columnNumber, kind);
    }

    private static ImportColumn ToColumn(string name, DetectedImportColumnType type)
        => new(name, type.Kind, type.LengthOrPrecision, type.Scale, type.IsNullable);
}
