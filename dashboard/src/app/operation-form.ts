import { HttpErrorResponse } from '@angular/common/http';

export function parseOperationDecimal(input: string): number | null {
  const value = input.trim();
  // Validate the original digits before converting to a JSON number.
  if (!/^(?:0*[0-9]{1,9}(?:\.[0-9]{0,6})?|\.[0-9]{1,6})$/.test(value)) return null;
  return Number(value);
}

export function taipeiToday(): string {
  return new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Taipei' }).format(new Date());
}

/** 作業與採收日期的上限：已結束的栽培為結束日，其他狀態為台北當日。 */
export function latestRecordDate(completionDate: string | null | undefined, today: string): string {
  return completionDate && completionDate < today ? completionDate : today;
}

/** 日期上限的說明文字，用於前端驗證訊息。 */
export function latestRecordLabel(completionDate: string | null | undefined, today: string): string {
  return completionDate && completionDate < today ? `結束日 ${completionDate}` : '今天';
}

export function operationError(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse) || ![400, 409].includes(error.status)) return fallback;
  const problem = error.error as { detail?: string; errors?: Record<string, string[]> } | null;
  return Object.values(problem?.errors ?? {}).flat().join(' ') || problem?.detail || fallback;
}

export type OperationSort<T extends string> = { column: T; direction: 'ascending' | 'descending' };

export function nextOperationSort<T extends string>(current: OperationSort<T>, column: T): OperationSort<T> {
  return { column, direction: current.column === column && current.direction === 'ascending' ? 'descending' : 'ascending' };
}

export function compareOperationValues(
  a: string | number | null, b: string | number | null, direction: OperationSort<string>['direction'],
): number {
  if (a === null) return b === null ? 0 : 1;
  if (b === null) return -1;
  const ordered = typeof a === 'number' && typeof b === 'number'
    ? a - b : String(a).localeCompare(String(b), 'zh-Hant-TW');
  return direction === 'ascending' ? ordered : -ordered;
}
