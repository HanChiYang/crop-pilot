import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './generated/api/auth.service';

const redirectErrors: Readonly<Record<string, string>> = {
  google_cancelled: '已取消 Google 登入。',
  google_sign_in_failed: '無法以 Google 帳號登入，請再試一次，或使用原帳號密碼登入。',
  use_existing_password: '這個 Email 已有 CropPilot 帳號，請使用原帳號密碼登入。',
  use_existing_google: '這個 Email 已有 CropPilot 帳號，請使用原先綁定的 Google 帳號登入。',
  use_existing_sign_in: '這個 Email 已有 CropPilot 帳號，請使用原帳號密碼或原先綁定的 Google 帳號登入。',
  account_pending: '請先完成原帳號的 Email 驗證。',
  session_unavailable: '目前無法確認登入狀態，請稍後再試。',
};

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <main class="login-layout">
      <aside class="login-intro">
        <a routerLink="/" class="brand"><span class="brand-mark">C</span> CropPilot</a>
        <div><h2>看清每一塊田</h2><p>在同一個地方整理田區和栽培計畫。</p></div>
      </aside>
      <section class="login-content" aria-labelledby="login-heading">
        <div class="login-form">
          <h1 id="login-heading">登入 CropPilot</h1>
          <p>使用農民帳號進入田區總覽</p>
          <form [formGroup]="form" (ngSubmit)="signIn()">
            <label for="username">帳號</label>
            <input id="username" type="text" formControlName="username" autocomplete="username" maxlength="256" required />
            <label for="password">密碼</label>
            <input id="password" type="password" formControlName="password" autocomplete="current-password" required />
            @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
            <button class="button" type="submit" [disabled]="busy()">{{ busy() ? '登入中…' : '登入' }}</button>
          </form>
          <div class="divider"><span>或</span></div>
          <a class="button button-outline google" href="/api/auth/google">以 Google 帳號登入</a>
        </div>
      </section>
    </main>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly redirectError = inject(ActivatedRoute).snapshot.queryParamMap.get('error');

  protected readonly busy = signal(false);
  protected readonly message = signal(
    this.redirectError && Object.hasOwn(redirectErrors, this.redirectError)
      ? redirectErrors[this.redirectError] : '',
  );
  protected readonly form = new FormGroup({
    username: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected async signIn(): Promise<void> {
    const { username, password } = this.form.getRawValue();
    if (this.busy()) return;
    if (this.form.invalid || !username.trim()) {
      this.message.set('請輸入帳號和密碼。');
      return;
    }

    this.busy.set(true);
    this.message.set('');
    try {
      await firstValueFrom(this.auth.getCsrfToken());
      await firstValueFrom(this.auth.login({ loginRequest: { username, password } }));
      await this.router.navigateByUrl('/dashboard', { replaceUrl: true });
    } catch (error: unknown) {
      const invalid = error instanceof HttpErrorResponse && error.status === 401;
      this.message.set(invalid ? '帳號或密碼不正確，請再試一次。' : '目前無法登入，請稍後再試。');
    } finally {
      this.busy.set(false);
    }
  }
}
