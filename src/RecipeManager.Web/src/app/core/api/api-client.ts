import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Observable, catchError, defer, map, of, shareReplay, switchMap, throwError } from 'rxjs';
import type { components } from './generated/schema';

export type ApiPath = `/api/v1/${string}`;
export interface ApiProblem {
  status: number;
  code: string;
  traceId?: string;
  errors?: { path: string; code: string; message: string }[];
  conflicts?: { resourceId: string; reason: string }[];
}

@Injectable({ providedIn: 'root' })
export class ApiClient {
  readonly authenticationRequired = signal(false);
  private csrf?: Observable<string>;
  private sessionEpoch = 0;
  private readonly http = inject(HttpClient);

  read<T>(path: ApiPath, etag?: string): Observable<HttpResponse<T>> {
    this.requireLocal(path);
    const epoch = this.sessionEpoch;
    return this.http
      .get<T>(path, {
        observe: 'response',
        withCredentials: true,
        headers: etag ? new HttpHeaders({ 'If-None-Match': etag }) : undefined,
      })
      .pipe(
        map((response) => {
          if (epoch !== this.sessionEpoch)
            throw new Error('Session changed; discard stale response.');
          return response;
        }),
        catchError((error: unknown) => {
          if (
            epoch === this.sessionEpoch &&
            error instanceof HttpErrorResponse &&
            error.status === 304
          )
            return of(
              new HttpResponse<T>({
                status: 304,
                headers: error.headers,
                url: error.url ?? undefined,
              }),
            );
          return this.failed(error, epoch);
        }),
      );
  }

  write<T>(
    method: 'POST' | 'PUT' | 'PATCH' | 'DELETE',
    path: ApiPath,
    body?: unknown,
    etag?: string,
    retryKey?: string,
  ): Observable<HttpResponse<T>> {
    this.requireLocal(path);
    const epoch = this.sessionEpoch;
    return defer(() => this.csrfToken()).pipe(
      switchMap((token) => {
        if (epoch !== this.sessionEpoch)
          return throwError(() => new Error('Session changed; retry explicitly.'));
        let headers = new HttpHeaders({ 'X-XSRF-TOKEN': token });
        if (etag) headers = headers.set('If-Match', etag);
        if (retryKey) headers = headers.set('Idempotency-Key', retryKey);
        return this.http.request<T>(method, path, {
          body,
          headers,
          observe: 'response',
          withCredentials: true,
        });
      }),
      map((response) => {
        if (epoch !== this.sessionEpoch)
          throw new Error('Session changed; discard stale response.');
        return response;
      }),
      catchError((error: unknown) => this.failed(error, epoch)),
    );
  }

  resetSession(): void {
    this.sessionEpoch++;
    this.csrf = undefined;
  }
  sessionEstablished(): void {
    this.resetSession();
    this.authenticationRequired.set(false);
  }

  private csrfToken(): Observable<string> {
    return (this.csrf ??= this.http
      .get<components['schemas']['CsrfBootstrap']>('/api/v1/auth/csrf', { withCredentials: true })
      .pipe(
        map((body) => body.token),
        catchError((error: unknown) => {
          this.csrf = undefined;
          return throwError(() => error);
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      ));
  }

  private failed(error: unknown, epoch: number): Observable<never> {
    if (epoch === this.sessionEpoch && error instanceof HttpErrorResponse && error.status === 401) {
      this.resetSession();
      this.authenticationRequired.set(true);
    }
    return throwError(() => error);
  }

  private requireLocal(path: string): void {
    if (!path.startsWith('/api/v1/') || path.includes('\\') || path.includes('#'))
      throw new Error('Only same-origin API paths are accepted.');
  }
}
