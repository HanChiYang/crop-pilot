import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthSession } from './auth-session';

@Component({
  selector: 'app-dashboard-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="overview-layout">
      <aside class="sidebar">
        <a routerLink="/" class="brand"
          ><span class="brand-mark">C</span> CropPilot</a
        >
        <nav aria-label="農場管理">
          <p>農場管理</p>
          <a
            routerLink="/dashboard"
            routerLinkActive="active"
            [routerLinkActiveOptions]="{ exact: true }"
            ariaCurrentWhenActive="page"
            >田區總覽</a
          >
          <a
            routerLink="/dashboard/fields"
            routerLinkActive="active"
            ariaCurrentWhenActive="page"
            >田區管理</a
          >
          <span aria-disabled="true">作物栽培管理</span>
        </nav>
        <div class="sidebar-account">
          <strong>農民帳號</strong>
          <button
            class="button button-outline"
            type="button"
            (click)="signOut()"
            [disabled]="busy()"
          >
            登出
          </button>
          @if (message()) {
            <p class="error" role="alert">{{ message() }}</p>
          }
        </div>
      </aside>
      <main class="overview-main">
        <router-outlet />
      </main>
    </div>
  `,
})
export class DashboardLayout {
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
        await this.router.navigate(['/login'], {
          replaceUrl: true,
          queryParams: { error: 'session_changed' },
        });
      } else {
        this.message.set('目前無法登出，請再試一次。');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
