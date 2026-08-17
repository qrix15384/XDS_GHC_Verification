import ExcelJS from "exceljs";
import { jsPDF } from "jspdf";
import autoTable from "jspdf-autotable";
import type { TransactionListItem } from "../types/api";

const COLUMNS: { header: string; key: keyof TransactionListItem; width: number }[] = [
  { header: "Time (UTC)", key: "requestAtUtc", width: 22 },
  { header: "Endpoint", key: "endpointPath", width: 38 },
  { header: "Method", key: "httpMethod", width: 10 },
  { header: "Username", key: "username", width: 20 },
  { header: "Subscriber", key: "subscriberName", width: 22 },
  { header: "Status", key: "httpStatusCode", width: 10 },
  { header: "Found", key: "detailsFound", width: 8 },
  { header: "PIN", key: "pinNumber", width: 20 },
  { header: "Duration (ms)", key: "durationMs", width: 14 },
];

function cellValue(item: TransactionListItem, key: keyof TransactionListItem): string {
  if (key === "requestAtUtc") return new Date(item.requestAtUtc).toLocaleString();
  const value = item[key];
  return value === null || value === undefined ? "" : String(value);
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

export async function exportTransactionsToExcel(items: TransactionListItem[], filenamePrefix: string) {
  const workbook = new ExcelJS.Workbook();
  const sheet = workbook.addWorksheet("Transactions");
  sheet.columns = COLUMNS.map((c) => ({ header: c.header, key: c.key, width: c.width }));
  sheet.getRow(1).font = { bold: true };

  for (const item of items) {
    sheet.addRow(Object.fromEntries(COLUMNS.map((c) => [c.key, cellValue(item, c.key)])));
  }

  const buffer = await workbook.xlsx.writeBuffer();
  downloadBlob(
    new Blob([buffer], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }),
    `${filenamePrefix}.xlsx`,
  );
}

export function exportTransactionsToPdf(items: TransactionListItem[], filenamePrefix: string) {
  const doc = new jsPDF({ orientation: "landscape" });
  doc.setFontSize(12);
  doc.text("XDS GHC Verification — Transactions", 14, 14);

  autoTable(doc, {
    startY: 20,
    head: [COLUMNS.map((c) => c.header)],
    body: items.map((item) => COLUMNS.map((c) => cellValue(item, c.key))),
    styles: { fontSize: 7 },
    headStyles: { fillColor: [43, 95, 217] },
  });

  doc.save(`${filenamePrefix}.pdf`);
}
