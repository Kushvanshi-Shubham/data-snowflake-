using OfficeOpenXml;
using System;
using System.IO;

class Program1
{
    static void Main1(string[] args)
    {
        string filePath = "path_to_your_large_excel_file.xlsx";

        try
        {
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                // Get the first worksheet
                var worksheet = package.Workbook.Worksheets[0];

                int rowCount = worksheet.Dimension.End.Row;
                int colCount = worksheet.Dimension.End.Column;

                // Stream through rows
                for (int row = 1; row <= rowCount; row++)
                {
                    for (int col = 1; col <= colCount; col++)
                    {
                        var cellValue = worksheet.Cells[row, col].Text;  // Read cell data
                        Console.Write(cellValue + "\t");
                    }

                    Console.WriteLine();
                    // Optionally, release memory periodically to avoid memory overflow
                    if (row % 1000 == 0)
                    {
                        GC.Collect(); // Trigger garbage collection to release unused memory
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
