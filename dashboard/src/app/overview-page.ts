import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthSession } from './auth-session';

@Component({
  selector: 'app-overview-page',
  imports: [RouterLink],
  template: `
    <div class="overview-layout">
      <aside class="sidebar">
        <a routerLink="/" class="brand"><span class="brand-mark">C</span> CropPilot</a>
        <nav aria-label="農場管理">
          <p>農場管理</p>
          <a routerLink="/dashboard" aria-current="page">田區總覽</a>
          <span aria-disabled="true">田區管理</span>
          <span aria-disabled="true">作物栽培管理</span>
        </nav>
        <div class="sidebar-account">
          <strong>農民帳號</strong>
          <button class="button button-outline" type="button" (click)="signOut()" [disabled]="busy()">登出</button>
          @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
        </div>
      </aside>
      <main class="overview-main">
        <h1>田區總覽</h1>
        <p>查看各田區的規劃、栽培與採收狀況</p>
        <section class="panel empty-state">
          <span class="empty-icon" aria-hidden="true">田</span>
          <h2>目前還沒有田區</h2>
          <p>先新增一塊田，就可以開始記錄要種的作物。</p>
          <button class="button" disabled>＋ 新增田區</button>
        </section>
      </main>
    </div>
  `,
})
export class OverviewPage {
  private readonly auth = inject(AuthSession);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly message = signal('');

  protected async signOut(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    try {
      await firstValueFrom(this.auth.signOut());
      await this.router.navigateByUrl('/login', { replaceUrl: true });
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        await this.router.navigate(['/login'], { replaceUrl: true, queryParams: { error: 'session_changed' } });
      } else {
        this.message.set('目前無法登出，請再試一次。');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
