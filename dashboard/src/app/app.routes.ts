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
import { PlantingHistoryPage } from './planting-history-page';
import { FertilizationPage } from './fertilization-page';
import { IrrigationPage } from './irrigation-page';
import { MaterialInventory } from './material-inventory';

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

const loadPlantingDetail = () => import('./planting-detail-page').then(m => m.PlantingDetailPage);

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
      { path: 'plantings', loadComponent: () => import('./planting-list-page').then((m) => m.PlantingListPage), title: '作物栽培｜CropPilot' },
      { path: 'plantings/new', component: PlantingFormPage, title: '新增栽培紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/new', component: PlantingFormPage, title: '新增栽培紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/:plantingId/edit', component: PlantingFormPage, title: '編輯栽培紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/:plantingId/fertilizations', component: FertilizationPage, title: '資材施用紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/:plantingId/irrigations', component: IrrigationPage, title: '灌溉紀錄｜CropPilot' },
      { path: 'plantings/:fieldId/:plantingId', loadComponent: loadPlantingDetail, title: '栽培詳情｜CropPilot' },
      { path: 'fertilizers', pathMatch: 'full', redirectTo: 'materials/fertilizers' },
      {
        path: 'materials',
        component: MaterialInventory,
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'pesticides' },
          {
            path: 'pesticides',
            loadComponent: () => import('./material-inventory-page').then(m => m.MaterialInventoryPage),
            data: { pesticide: true },
            title: '農藥資材管理｜CropPilot',
          },
          {
            path: 'fertilizers',
            loadComponent: () => import('./material-inventory-page').then(m => m.MaterialInventoryPage),
            title: '肥料資材管理｜CropPilot',
          },
        ],
      },
      {
        path: 'planting-history',
        component: PlantingHistoryPage,
        title: '栽培歷史｜CropPilot',
        children: [
          { path: ':fieldId/:plantingId', loadComponent: loadPlantingDetail, data: { fromHistory: true }, title: '栽培紀錄詳情｜CropPilot' },
          {
            path: ':fieldId/:plantingId/fertilizations',
            component: FertilizationPage,
            data: { fromHistory: true },
            title: '資材施用紀錄｜CropPilot',
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
