import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-material-inventory',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  styles: `
    nav { display: flex; gap: .5rem; margin-top: 1rem; }
    .button { min-height: 2.25rem; padding: .3rem 1.25rem; font-weight: 400; }
    .active { background: var(--pale); font-weight: 700; }
  `,
  template: `
    <header class="page-heading"><h1>資材管理</h1></header>
    <nav aria-label="資材類別">
      <a class="button button-outline" routerLink="pesticides" routerLinkActive="active"
        ariaCurrentWhenActive="page">農藥</a>
      <a class="button button-outline" routerLink="fertilizers" routerLinkActive="active"
        ariaCurrentWhenActive="page">肥料</a>
    </nav>
    <router-outlet />
  `,
})
export class MaterialInventory {}
