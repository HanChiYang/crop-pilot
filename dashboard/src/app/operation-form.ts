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
