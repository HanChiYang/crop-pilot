import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router, Routes } from '@angular/router';
import { catchError, map, of, timeout } from 'rxjs';
import { AuthService } from './generated/api/auth.service';
import { LandingPage } from './landing-page';
import { LoginPage } from './login-page';
import { DashboardLayout } from './dashboard-layout';
import { FieldFormPage } from './field-form-page';
import { FieldListPage } from './field-list-page';
import { PlantingFormPage } from './planting-form-page';
import { PlantingListPage } from './planting-list-page';
import { PlantingHistoryPage } from './planting-history-page';
import { PlantingHistoryDetailPage } from './planting-history-detail-page';
import { FertilizationPage } from './fertilization-page';
import { IrrigationPage } from './irrigation-page';
import { FertilizerInventoryPage } from './fertilizer-inventory-page';

const redirectSignedIn: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.getSession().pipe(
    timeout(5_000),
    map(() => new RedirectCommand(router.createUrlTree(['/dashboard']), { replaceUrl: true })),
    catchError(() => of(true)),
  );
};

const requireSession: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.getSession().pipe(
    timeout(5_000),
    map(() => true),
    catchError((error: unknown) => {
      const expired = error instanceof HttpErrorResponse && error.status === 401;
      return of(router.createUrlTree(['/login'], {
        queryParams: expired ? {} : { error: 'session_unavailable' },
      }));
    }),
  );
};

export const routes: Routes = [
  { path: '', component: LandingPage, canActivate: [redirectSignedIn], title: 'CropPilot' },
  { path: 'login', component: LoginPage, title: '登入 CropPilot' },
  {
    path: 'dashboard',
    component: DashboardLayout,
    canActivateChild: [requireSession],
    children: [
      { path: '', component: FieldListPage, data: { overview: true }, title: '田區總覽｜CropPilot' },
      { path: 'fields', component: FieldListPage, title: '田區管理｜CropPilot' },
      { path: 'fields/new', component: FieldFormPage, title: '新增田區｜CropPilot' },
      { path: 'fields/:fieldId/edit', component: FieldFormPage, title: '編輯田區｜CropPilot' },
      { path: 'plantings', component: PlantingListPage, title: '作物栽培管理｜CropPilot' },
      { path: 'plantings/:fieldId/new', component: PlantingFormPage, title: '新增栽培紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/:plantingId/edit', component: PlantingFormPage, title: '編輯栽培紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/:plantingId/fertilizations', component: FertilizationPage, title: '施肥紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/:plantingId/irrigations', component: IrrigationPage, title: '灌溉紀錄｜CropPilot' },
      { path: 'fertilizers', component: FertilizerInventoryPage, title: '肥料庫存｜CropPilot' },
      {
        path: 'planting-history',
        component: PlantingHistoryPage,
        title: '栽培歷史｜CropPilot',
        children: [
          { path: ':fieldId/:plantingId', component: PlantingHistoryDetailPage, title: '栽培紀錄詳情｜CropPilot' },
          {
            path: ':fieldId/:plantingId/fertilizations',
            component: FertilizationPage,
            data: { fromHistory: true },
            title: '施肥紀錄｜CropPilot',
          },
          {
            path: ':fieldId/:plantingId/irrigations',
            component: IrrigationPage,
            data: { fromHistory: true },
            title: '灌溉紀錄｜CropPilot',
          },
        ],
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
