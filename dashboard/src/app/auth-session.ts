import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { DOCUMENT, inject, Injectable } from '@angular/core';
import { catchError, finalize, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';
import { AuthService } from './generated/api/auth.service';
import { AccessTokenRsDto, LoginRqDto } from './generated/model/models';

@Injectable({ providedIn: 'root' })
export class AuthSession {
  private readonly auth = inject(AuthService);
  private credentials?: AccessTokenRsDto;
  private renewal?: Observable<AccessTokenRsDto>;
  private revision = 0;

  get access(): Readonly<AccessTokenRsDto> | undefined {
    return this.credentials;
  }

  signIn(loginRqDto: LoginRqDto): Observable<AccessTokenRsDto> {
    return this.auth.getCsrfToken().pipe(
      switchMap(() => this.auth.login({ loginRqDto })),
      tap(credentials => {
        this.revision++;
        this.credentials = credentials;
      }),
    );
  }

  signOut(): Observable<void> {
    const credentials = this.credentials;
    if (!credentials) {
      return of(undefined);
    }

    return this.auth.getCsrfToken().pipe(
      switchMap(() => this.auth.logout({ logoutRqDto: { sessionId: credentials.sessionId } })),
      catchError((error: unknown) => isUnauthorized(error) ? of(undefined) : throwError(() => error)),
      tap({
        next: () => this.clearAccess(),
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 409) {
            this.clearAccess();
          }
        },
      }),
    );
  }

  private clearAccess(): void {
    this.revision++;
    this.credentials = undefined;
  }

  refresh(previous?: Readonly<AccessTokenRsDto>): Observable<string> {
    const credentials = this.credentials;
    if (previous && previous.sessionId !== credentials?.sessionId) {
      return throwError(sessionChanged);
    }
    if (credentials && previous?.accessToken !== credentials.accessToken) {
      return of(credentials.accessToken);
    }

    if (!this.renewal) {
      const revision = this.revision;
      this.renewal = this.auth.getCsrfToken().pipe(
        switchMap(() => this.auth.refreshAccessToken()),
        tap(next => {
          if (revision !== this.revision || (credentials && credentials.sessionId !== next.sessionId)) {
            throw sessionChanged();
          }
          this.credentials = next;
        }),
        finalize(() => { this.renewal = undefined; }),
        shareReplay({ bufferSize: 1, refCount: true }),
      );
    }

    return this.renewal.pipe(map(response => response.accessToken));
  }
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 401;
}

function sessionChanged(): HttpErrorResponse {
  return new HttpErrorResponse({ status: 401, statusText: 'Login session changed' });
}

export const bearerInterceptor: HttpInterceptorFn = (request, next) => {
  const origin = inject(DOCUMENT).location.origin;
  const { origin: target, pathname: path } = new URL(request.url, origin);
  if (target !== origin || !path.startsWith('/api/v1/')
    || (path.startsWith('/api/v1/auth/') && path !== '/api/v1/auth/session')) {
    return next(request);
  }

  const session = inject(AuthSession);
  const send = (token: string) => next(request.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  }));
  const access = session.access;
  if (!access) {
    return session.refresh().pipe(switchMap(send));
  }

  return send(access.accessToken).pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse && error.status === 401
      && error.headers.get('WWW-Authenticate') === 'Bearer') {
      return session.refresh(access).pipe(switchMap(send));
    }
    return throwError(() => error);
  }));
};
