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
