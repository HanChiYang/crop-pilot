import { DecimalPipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, of, switchMap } from 'rxjs';
import { MaterialsService } from './generated/api/materials.service';
import { PlantingsService } from './generated/api/plantings.service';
import { MaterialApplicationRsDto } from './generated/model/materialApplicationRsDto';
import { FertilizerLotRsDto } from './generated/model/fertilizerLotRsDto';
import { FertilizerMaterialRsDto } from './generated/model/fertilizerMaterialRsDto';
import { MaterialApplicationMethod } from './generated/model/materialApplicationMethod';
import { PesticideLotRsDto } from './generated/model/pesticideLotRsDto';
import { HttpErrorResponse } from '@angular/common/http';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { operationError, parseOperationDecimal, taipeiToday } from './operation-form';

type MaterialType = 'fertilizer' | 'pesticide';
const pesticideUnits = { liter: '公升', milliliter: '毫升', kilogram: '公斤', gram: '公克' };

interface DetailInput {
  materialType: MaterialType;
  method: MaterialApplicationMethod | '';
  id?: string;
  original?: { lotId: string; unitPrice: number | null };
  materialId: string;
  lotId: string;
  quantity: string;
}

@Component({
  selector: 'app-fertilization-page',
  imports: [DecimalPipe, FormsModule, RouterLink],
  styles: `
    .actions { display: flex; flex-wrap: wrap; gap: .65rem; }
    .details { display: grid; gap: .7rem; margin-block: 1rem; }
    .detail { display: grid; grid-template-columns: repeat(2, minmax(6rem, 1fr)) minmax(0, 2fr) repeat(2, minmax(6rem, 1fr)) auto; gap: .75rem; align-items: end; }
    .entry { padding: 1rem; }
    .entry + .entry { border-top: 1px solid var(--border); }
    .entry ul { padding-inline-start: 1.4rem; }
    @media (max-width: 650px) { .detail { grid-template-columns: 1fr; } }
  `,
  template: `
    <header class="page-heading">
      <div><h1>資材施用紀錄</h1>@if (planting(); as item) { <p>{{ item.cropName }}</p> }</div>
      <div class="actions">
        <a class="button button-outline" [routerLink]="backLink()" [queryParams]="backQuery()">返回{{ fromHistory ? '栽培詳情' : '作物栽培管理' }}</a>
        <a class="button button-outline" routerLink="/dashboard/fertilizers">肥料庫存</a>
        @if (!loading() && !loadError() && planting() && planting()?.status !== 'planned' && !editorOpen()) {
          <button class="button" type="button" (click)="open()">新增施用</button>
        }
      </div>
    </header>

    @if (loading()) {
      <p class="panel empty-state" role="status">正在載入資材施用紀錄…</p>
    } @else if (loadError()) {
      <section class="panel empty-state">
        <p class="error" role="alert">{{ loadError() }}</p>
        <button class="button button-outline" type="button" (click)="load()">重新載入</button>
      </section>
    } @else if (editorOpen()) {
      <form class="panel field-form" (ngSubmit)="save()">
        <h2>{{ selected() ? '編輯施用' : '新增施用' }}</h2>
        <fieldset [disabled]="busy()">
          <div class="field-control">
            <label for="operation-date">施用日期</label>
            <input id="operation-date" type="date" name="operationDate" required [max]="today"
              [ngModel]="operationDate()" (ngModelChange)="operationDate.set($event)">
          </div>
          @if (hasSpray()) {
            <div class="field-control">
              <label for="spray-water-volume">噴施總水量（公升）</label>
              <input id="spray-water-volume" name="sprayWaterVolumeLiters" inputmode="decimal"
                [ngModel]="sprayWater()" (ngModelChange)="sprayWater.set($event)">
            </div>
          }
          @if (!availableLots().length) {
            <p>該日沒有可用批次。<a routerLink="/dashboard/fertilizers">查看肥料庫存</a></p>
          }
          <div class="details">
            @for (row of rows(); track $index; let index = $index) {
              <div class="detail">
                <div class="field-control">
                  <label [for]="'material-type-' + index">資材類別</label>
                  <select [id]="'material-type-' + index" [name]="'materialType-' + index"
                    [ngModel]="row.materialType" (ngModelChange)="selectType(row, $event)">
                    <option value="fertilizer">肥料</option><option value="pesticide">農藥</option>
                  </select>
                </div>
                <div class="field-control">
                  <label [for]="'fertilizer-material-' + index">資材 {{ index + 1 }}</label>
                  <select [id]="'fertilizer-material-' + index" [name]="'material-' + index" required
                    [ngModel]="row.materialId" (ngModelChange)="selectMaterial(row, $event)">
                    <option value="">請選擇資材</option>
                    @for (material of availableMaterials(row.materialType); track material.id) {
                      <option [value]="material.id">{{ material.name }}</option>
                    }
                    @if (row.materialId && !availableMaterialIds().has(row.materialId)) {
                      <option [value]="row.materialId" disabled>原選資材於該日無可用批次</option>
                    }
                  </select>
                </div>
                <div class="field-control">
                  <label [for]="'fertilizer-lot-' + index">庫存批次 {{ index + 1 }}</label>
                  <select [id]="'fertilizer-lot-' + index" [name]="'lot-' + index" required
                    [disabled]="!row.materialId"
                    [ngModel]="row.lotId" (ngModelChange)="row.lotId = $event">
                    <option value="">請選擇庫存批次</option>
                    @for (lot of lotsByMaterial().get(row.materialId) ?? []; track lot.id) {
                      @let retained = row.original?.lotId === lot.id;
                      @let price = retained ? row.original?.unitPrice : lot.unitPrice;
                      <option [value]="lot.id">{{ lot.label }}｜可用 {{ lot.available | number:'1.0-6' }} {{ lot.unit }}｜{{ retained ? '原單價' : '批次單價' }} {{ price == null ? '成本待補' : 'NT$' + price }}</option>
                    }
                    @if (row.lotId && !availableLotIds().has(row.lotId)) {
                      <option [value]="row.lotId" disabled>原選批次於該日無法使用</option>
                    }
                  </select>
                </div>
                <div class="field-control">
                  <label [for]="'method-' + index">施用方式</label>
                  <select [id]="'method-' + index" [name]="'method-' + index" required
                    [ngModel]="row.method" (ngModelChange)="selectMethod(row, $event)">
                    <option value="">請選擇方式</option>
                    <option value="spray">噴施</option><option value="broadcast">撒施</option>
                  </select>
                </div>
                <div class="field-control">
                  <label [for]="'fertilizer-quantity-' + index">用量</label>
                  <input [id]="'fertilizer-quantity-' + index" [name]="'quantity-' + index"
                    inputmode="decimal" required [(ngModel)]="row.quantity">
                  @if (unitFor(row.lotId); as unit) { <span>{{ unit }}</span> }
                  @if (detailErrors()[index]; as error) { <small class="error" role="alert">{{ error }}</small> }
                </div>
                <button class="button button-outline" type="button" [disabled]="rows().length === 1"
                  [attr.aria-label]="'移除第 ' + (index + 1) + ' 項資材'" (click)="removeRow(index)">移除</button>
              </div>
            }
          </div>
          <div class="actions">
            <button class="button button-outline" type="button" (click)="addRow()">新增肥料明細</button>
            <button class="button button-outline" type="button" (click)="addRow('pesticide')">新增農藥明細</button>
          </div>
          @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
          <div class="field-form-actions">
            <button class="button button-outline" type="button" (click)="editorOpen.set(false)">取消</button>
            <button class="button" type="submit">儲存</button>
          </div>
        </fieldset>
      </form>
    } @else if (!operations().length) {
      <p class="panel empty-state">目前沒有資材施用紀錄。</p>
    } @else {
      <section class="panel" aria-label="資材施用紀錄清單">
        @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
        @for (operation of operations(); track operation.id) {
          <article class="entry">
            <h2><time [attr.datetime]="operation.operationDate">{{ operation.operationDate }}</time></h2>
            <p>噴施總水量
              @if (operation.sprayWaterVolumeLiters != null) {
                {{ operation.sprayWaterVolumeLiters | number:'1.0-6' }} 公升
              } @else {
                {{ operationUsesSpray(operation) ? '未記錄' : '不適用' }}
              }
            </p>
            <ul>
              @for (detail of operation.details; track detail.id) {
                <li>{{ detail.materialName }} · {{ detail.applicationMethod === 'spray' ? '噴施' : '撒施' }} · {{ detail.quantity | number:'1.0-6' }} {{ detail.unit }}
                  @if (detail.unitPrice === null) { · 成本待補 }
                </li>
              }
            </ul>
            <button class="button button-outline" type="button" [disabled]="busy()" (click)="open(operation)">編輯</button>
            @if (pendingDelete() === operation.id) {
              <span>刪除此筆施用紀錄並退回批次用量？</span>
              <button class="button button-outline" type="button" [disabled]="busy()" (click)="delete(operation.id)">確認刪除</button>
              <button class="button button-outline" type="button" [disabled]="busy()" (click)="pendingDelete.set(null)">取消</button>
            } @else {
              <button class="button button-outline" type="button" [disabled]="busy()" (click)="pendingDelete.set(operation.id)">刪除</button>
            }
          </article>
        }
      </section>
    }
  `,
})
export class FertilizationPage {
  private readonly route = inject(ActivatedRoute);
  private readonly plantings = inject(PlantingsService);
  private readonly materialsApi = inject(MaterialsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ids = signal({ fieldId: '', plantingId: '' });
  private requestVersion = 0;
  private readonly inventory = signal<{ material: FertilizerMaterialRsDto; type: MaterialType; lots: (FertilizerLotRsDto | PesticideLotRsDto)[] }[]>([]);

  protected readonly fromHistory = this.route.snapshot.data['fromHistory'] === true;
  protected readonly backLink = computed(() => this.fromHistory
    ? ['/dashboard/planting-history', this.ids().fieldId, this.ids().plantingId]
    : ['/dashboard/plantings']);
  protected readonly backQuery = computed(() => this.fromHistory ? {} : { fieldId: this.ids().fieldId });
  protected readonly planting = signal<PlantingRsDto | null>(null);
  protected readonly operations = signal<MaterialApplicationRsDto[]>([]);
  protected readonly today = taipeiToday();
  protected readonly operationDate = signal(this.today);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly editorOpen = signal(false);
  protected readonly loadError = signal('');
  protected readonly message = signal('');
  protected readonly selected = signal<MaterialApplicationRsDto | null>(null);
  protected readonly rows = signal<DetailInput[]>([]);
  protected readonly sprayWater = signal('');
  protected readonly detailErrors = signal<Record<number, string>>({});
  protected readonly pendingDelete = signal<string | null>(null);
  protected readonly hasSpray = computed(() => this.rows().some(row => row.method === 'spray'));

  protected readonly availableLots = computed(() => {
    const date = this.operationDate();
    const original = new Map<string, number>();
    for (const detail of this.selected()?.details ?? []) {
      original.set(detail.materialPurchaseLotId, (original.get(detail.materialPurchaseLotId) ?? 0) + detail.quantity);
    }
    return this.inventory().flatMap(({ material, lots }) => lots.flatMap(lot => {
      const available = lot.remainingQuantity + (original.get(lot.id) ?? 0);
      if (!date || lot.availableFrom > date || available <= 0) return [];
      const source = lot.source === 'purchase' ? '進貨' : '期初庫存';
      return [{ id: lot.id, materialId: material.id, available, unit: material.stockUnit, unitPrice: lot.unitPrice,
        label: [source, lot.availableFrom].join('｜') }];
    }));
  });
  protected readonly lotsByMaterial = computed(() => {
    const lots = this.availableLots();
    const byMaterial = new Map<string, typeof lots>();
    for (const lot of lots) {
      const group = byMaterial.get(lot.materialId);
      if (group) group.push(lot);
      else byMaterial.set(lot.materialId, [lot]);
    }
    return byMaterial;
  });
  protected availableMaterials(type: MaterialType) {
    return this.inventory().filter(item => item.type === type && this.lotsByMaterial().has(item.material.id))
      .map(item => item.material);
  }
  protected readonly availableMaterialIds = computed(() => new Set(
    this.inventory().filter(item => this.lotsByMaterial().has(item.material.id)).map(item => item.material.id)));
  protected readonly availableLotIds = computed(() => new Set(this.availableLots().map(lot => lot.id)));

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.ids.set({ fieldId: params.get('fieldId')!, plantingId: params.get('plantingId')! });
      this.editorOpen.set(false);
      this.planting.set(null);
      void this.load();
    });
  }

  protected async load(): Promise<void> {
    const version = ++this.requestVersion;
    const ids = this.ids();
    this.loading.set(true);
    this.loadError.set('');
    try {
      const data = await firstValueFrom(forkJoin({
        planting: this.plantings.getPlanting({ plantingId: ids.plantingId }),
        operations: this.plantings.listMaterialApplications({ plantingId: ids.plantingId }),
        inventory: this.materialsApi.listFertilizerMaterials().pipe(switchMap(materials =>
          materials.length ? forkJoin(materials.map(material =>
            forkJoin({ material: of(material), type: of('fertilizer' as const), lots: this.materialsApi.listFertilizerLots({ materialId: material.id }) })))
            : of([]))),
        pesticides: this.materialsApi.listPesticideMaterials().pipe(switchMap(materials =>
          materials.length ? forkJoin(materials.map(material => forkJoin({
            material: of({ ...material, stockUnit: pesticideUnits[material.stockUnit] }),
            type: of('pesticide' as const),
            lots: this.materialsApi.listPesticideLots({ materialId: material.id }),
          }))) : of([]))),
      }).pipe(takeUntilDestroyed(this.destroyRef)));
      if (version !== this.requestVersion) return;
      if (data.planting.fieldId !== ids.fieldId.toLowerCase()) {
        this.loadError.set('找不到這筆栽培紀錄。');
        return;
      }
      this.planting.set(data.planting);
      this.operations.set(data.operations);
      this.inventory.set([...data.inventory, ...data.pesticides]);
    } catch {
      if (version === this.requestVersion) this.loadError.set('無法載入資材施用紀錄及肥料庫存。');
    } finally {
      if (version === this.requestVersion) this.loading.set(false);
    }
  }

  protected open(operation?: MaterialApplicationRsDto): void {
    this.selected.set(operation ?? null);
    this.detailErrors.set({});
    this.pendingDelete.set(null);
    this.sprayWater.set(operation?.sprayWaterVolumeLiters == null ? '' : String(operation.sprayWaterVolumeLiters));
    this.operationDate.set(operation?.operationDate ?? this.today);
    this.rows.set(operation?.details.map(detail => ({
      id: detail.id, materialType: detail.materialType, method: detail.applicationMethod, materialId: detail.materialId, lotId: detail.materialPurchaseLotId, quantity: String(detail.quantity),
      original: { lotId: detail.materialPurchaseLotId, unitPrice: detail.unitPrice },
    })) ?? [{ materialType: 'fertilizer', method: '', materialId: '', lotId: '', quantity: '' }]);
    this.message.set('');
    this.editorOpen.set(true);
  }

  protected addRow(materialType: MaterialType = 'fertilizer'): void {
    this.rows.update(rows => [...rows, { materialType, method: '', materialId: '', lotId: '', quantity: '' }]);
  }

  protected selectType(row: DetailInput, type: MaterialType): void {
    row.materialType = type;
    this.selectMaterial(row, '');
  }

  protected selectMethod(row: DetailInput, method: MaterialApplicationMethod | ''): void {
    this.rows.update(rows => rows.map(item => item === row ? { ...item, method } : item));
    if (!this.hasSpray()) this.sprayWater.set('');
  }

  protected unitFor(lotId: string): string {
    return this.availableLots().find(lot => lot.id === lotId)?.unit ?? '';
  }

  protected operationUsesSpray(operation: MaterialApplicationRsDto): boolean {
    return operation.details.some(detail => detail.applicationMethod === 'spray');
  }

  protected selectMaterial(row: DetailInput, materialId: string): void {
    row.materialId = materialId;
    row.lotId = '';
  }

  protected removeRow(index: number): void {
    this.rows.update(rows => rows.filter((_, rowIndex) => rowIndex !== index));
    this.detailErrors.set({});
    if (!this.hasSpray()) this.sprayWater.set('');
  }

  protected async save(): Promise<void> {
    if (this.busy()) return;
    const operationDate = this.operationDate();
    if (!operationDate || operationDate > this.today) {
      this.message.set('請確認施用日期。');
      return;
    }
    this.detailErrors.set({});
    const sprayWaterVolumeLiters = this.sprayWater().trim() ? parseOperationDecimal(this.sprayWater()) : null;
    if (this.sprayWater().trim() && (sprayWaterVolumeLiters === null || sprayWaterVolumeLiters <= 0)) {
      this.message.set('噴施總水量須大於零、小於 10 億，且最多六位小數。');
      return;
    }
    const details = [];
    for (const row of this.rows()) {
      const quantity = parseOperationDecimal(row.quantity);
      if (!row.method || !this.availableLotIds().has(row.lotId) || quantity === null || quantity <= 0) {
        this.message.set('請選擇施用方式及可用的庫存批次。用量須大於零、小於 10 億，且最多六位小數。');
        return;
      }
      details.push({ id: row.id, materialPurchaseLotId: row.lotId, applicationMethod: row.method, quantity });
    }
    const selected = this.selected();
    const ids = this.ids();
    this.busy.set(true);
    this.message.set('');
    try {
      await firstValueFrom((selected
        ? this.plantings.editMaterialApplication({ materialApplicationId: selected.id, editMaterialApplicationRqDto: { operationDate, sprayWaterVolumeLiters, details } })
        : this.plantings.createMaterialApplication({ plantingId: ids.plantingId, createMaterialApplicationRqDto: { operationDate, sprayWaterVolumeLiters, details } }))
        .pipe(takeUntilDestroyed(this.destroyRef)));
      if (ids !== this.ids()) return;
      this.editorOpen.set(false);
      await this.load();
    } catch (error) {
      if (ids !== this.ids()) return;
      const problem = error instanceof HttpErrorResponse ? error.error : null;
      const rowErrors: Record<number, string> = {};
      const errors = (problem?.errors ?? {}) as Record<string, string[]>;
      for (const [field, messages] of Object.entries(errors)) {
        const index = /^details\[(\d+)\](?:\.|$)/.exec(field)?.[1];
        if (index !== undefined) rowErrors[Number(index)] = messages.join(' ');
      }
      this.detailErrors.set(rowErrors);
      if (!Object.keys(rowErrors).length)
        this.message.set(operationError(error, '無法儲存資材施用紀錄，請再試一次。'));
    } finally {
      this.busy.set(false);
    }
  }
  protected async delete(id: string): Promise<void> {
    if (this.busy()) return;
    const ids = this.ids();
    this.busy.set(true);
    this.message.set('');
    try {
      await firstValueFrom(this.plantings.deleteMaterialApplication({ materialApplicationId: id })
        .pipe(takeUntilDestroyed(this.destroyRef)));
      if (ids !== this.ids()) return;
      this.pendingDelete.set(null);
      await this.load();
    } catch (error) {
      if (ids === this.ids()) this.message.set(operationError(error, '無法刪除資材施用紀錄，請再試一次。'));
    } finally {
      this.busy.set(false);
    }
  }
}
