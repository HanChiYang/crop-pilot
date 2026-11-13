import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-material-inventory',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  styles: `
    nav { display: flex; gap: .5rem; margin-bottom: 1rem; }
    .active { background: var(--green-100, #e8f0e8); font-weight: 700; }
  `,
  template: `
    <nav aria-label="資材庫存">
      <a class="button button-outline" routerLink="pesticides" routerLinkActive="active"
        ariaCurrentWhenActive="page">農藥庫存</a>
      <a class="button button-outline" routerLink="fertilizers" routerLinkActive="active"
        ariaCurrentWhenActive="page">肥料庫存</a>
    </nav>
    <router-outlet />
  `,
})
export class MaterialInventory {}
