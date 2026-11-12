import { DecimalPipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, of, switchMap } from 'rxjs';
import { MaterialsService } from './generated/api/materials.service';
import { PlantingsService } from './generated/api/plantings.service';
import { FertilizationRsDto } from './generated/model/fertilizationRsDto';
import { FertilizerLotRsDto } from './generated/model/fertilizerLotRsDto';
import { FertilizerMaterialRsDto } from './generated/model/fertilizerMaterialRsDto';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { operationError, parseOperationDecimal, taipeiToday } from './operation-form';

interface DetailInput {
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
    .detail { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 3fr) minmax(5rem, 1fr) auto; gap: .75rem; align-items: end; }
    .entry { padding: 1rem; }
    .entry + .entry { border-top: 1px solid var(--border); }
    .entry ul { padding-inline-start: 1.4rem; }
    @media (max-width: 650px) { .detail { grid-template-columns: 1fr; } }
  `,
  template: `
    <header class="page-heading">
      <div><h1>施肥紀錄</h1>@if (planting(); as item) { <p>{{ item.cropName }}</p> }</div>
      <div class="actions">
        <a class="button button-outline" [routerLink]="backLink()" [queryParams]="backQuery()">返回{{ fromHistory ? '栽培詳情' : '作物栽培管理' }}</a>
        <a class="button button-outline" routerLink="/dashboard/fertilizers">肥料庫存</a>
        @if (!loading() && !loadError() && planting() && planting()?.status !== 'planned' && !editorOpen()) {
          <button class="button" type="button" (click)="open()">新增施肥</button>
        }
      </div>
    </header>

    @if (loading()) {
      <p class="panel empty-state" role="status">正在載入施肥紀錄…</p>
    } @else if (loadError()) {
      <section class="panel empty-state">
        <p class="error" role="alert">{{ loadError() }}</p>
        <button class="button button-outline" type="button" (click)="load()">重新載入</button>
      </section>
    } @else if (editorOpen()) {
      <form class="panel field-form" (ngSubmit)="save()">
        <h2>{{ selected() ? '編輯施肥' : '新增施肥' }}</h2>
        <fieldset [disabled]="busy()">
          <div class="field-control">
            <label for="operation-date">施肥日期</label>
            <input id="operation-date" type="date" name="operationDate" required [max]="today"
              [ngModel]="operationDate()" (ngModelChange)="operationDate.set($event)">
          </div>
          @if (!availableLots().length) {
            <p>該日沒有可用批次。<a routerLink="/dashboard/fertilizers">查看肥料庫存</a></p>
          }
          <div class="details">
            @for (row of rows(); track $index; let index = $index) {
              <div class="detail">
                <div class="field-control">
                  <label [for]="'fertilizer-material-' + index">肥料 {{ index + 1 }}</label>
                  <select [id]="'fertilizer-material-' + index" [name]="'material-' + index" required
                    [ngModel]="row.materialId" (ngModelChange)="selectMaterial(row, $event)">
                    <option value="">請選擇肥料</option>
                    @for (material of availableMaterials(); track material.id) {
                      <option [value]="material.id">{{ material.name }}</option>
                    }
                    @if (row.materialId && !availableMaterialIds().has(row.materialId)) {
                      <option [value]="row.materialId" disabled>原選肥料於該日無可用批次</option>
                    }
                  </select>
                </div>
                <div class="field-control">
                  <label [for]="'fertilizer-lot-' + index">肥料批次 {{ index + 1 }}</label>
                  <select [id]="'fertilizer-lot-' + index" [name]="'lot-' + index" required
                    [disabled]="!row.materialId"
                    [ngModel]="row.lotId" (ngModelChange)="row.lotId = $event">
                    <option value="">請選擇肥料批次</option>
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
                  <label [for]="'fertilizer-quantity-' + index">用量</label>
                  <input [id]="'fertilizer-quantity-' + index" [name]="'quantity-' + index"
                    inputmode="decimal" required [(ngModel)]="row.quantity">
                </div>
                <button class="button button-outline" type="button" [disabled]="rows().length === 1"
                  [attr.aria-label]="'移除第 ' + (index + 1) + ' 項肥料'" (click)="removeRow(index)">移除</button>
              </div>
            }
          </div>
          <div class="actions">
            <button class="button button-outline" type="button" (click)="addRow()">新增一項肥料</button>
          </div>
          @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
          <div class="field-form-actions">
            <button class="button button-outline" type="button" (click)="editorOpen.set(false)">取消</button>
            <button class="button" type="submit">儲存</button>
          </div>
        </fieldset>
      </form>
    } @else if (!operations().length) {
      <p class="panel empty-state">目前沒有施肥紀錄。</p>
    } @else {
      <section class="panel" aria-label="施肥紀錄清單">
        @for (operation of operations(); track operation.id) {
          <article class="entry">
            <h2><time [attr.datetime]="operation.operationDate">{{ operation.operationDate }}</time></h2>
            <ul>
              @for (detail of operation.details; track detail.id) {
                <li>{{ detail.fertilizerName }} · {{ detail.quantity | number:'1.0-6' }} {{ detail.unit }}
                  @if (detail.unitPrice === null) { · 成本待補 }
                </li>
              }
            </ul>
            <button class="button button-outline" type="button" (click)="open(operation)">編輯</button>
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
  private readonly inventory = signal<{ material: FertilizerMaterialRsDto; lots: FertilizerLotRsDto[] }[]>([]);

  protected readonly fromHistory = this.route.snapshot.data['fromHistory'] === true;
  protected readonly backLink = computed(() => this.fromHistory
    ? ['/dashboard/planting-history', this.ids().fieldId, this.ids().plantingId]
    : ['/dashboard/plantings']);
  protected readonly backQuery = computed(() => this.fromHistory ? {} : { fieldId: this.ids().fieldId });
  protected readonly planting = signal<PlantingRsDto | null>(null);
  protected readonly operations = signal<FertilizationRsDto[]>([]);
  protected readonly today = taipeiToday();
  protected readonly operationDate = signal(this.today);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly editorOpen = signal(false);
  protected readonly loadError = signal('');
  protected readonly message = signal('');
  protected readonly selected = signal<FertilizationRsDto | null>(null);
  protected readonly rows = signal<DetailInput[]>([]);

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
  protected readonly availableMaterials = computed(() => this.inventory()
    .map(item => item.material).filter(material => this.lotsByMaterial().has(material.id)));
  protected readonly availableMaterialIds = computed(() => new Set(this.availableMaterials().map(material => material.id)));
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
        planting: this.plantings.getPlanting(ids),
        operations: this.plantings.listFertilizations(ids),
        inventory: this.materialsApi.listFertilizerMaterials().pipe(switchMap(materials =>
          materials.length ? forkJoin(materials.map(material =>
            forkJoin({ material: of(material), lots: this.materialsApi.listFertilizerLots({ materialId: material.id }) })))
            : of([]))),
      }).pipe(takeUntilDestroyed(this.destroyRef)));
      if (version !== this.requestVersion) return;
      this.planting.set(data.planting);
      this.operations.set(data.operations);
      this.inventory.set(data.inventory);
    } catch {
      if (version === this.requestVersion) this.loadError.set('無法載入施肥紀錄及肥料庫存。');
    } finally {
      if (version === this.requestVersion) this.loading.set(false);
    }
  }

  protected open(operation?: FertilizationRsDto): void {
    this.selected.set(operation ?? null);
    this.operationDate.set(operation?.operationDate ?? this.today);
    this.rows.set(operation?.details.map(detail => ({
      id: detail.id, materialId: detail.materialId, lotId: detail.materialPurchaseLotId, quantity: String(detail.quantity),
      original: { lotId: detail.materialPurchaseLotId, unitPrice: detail.unitPrice },
    })) ?? [{ materialId: '', lotId: '', quantity: '' }]);
    this.message.set('');
    this.editorOpen.set(true);
  }

  protected addRow(): void {
    this.rows.update(rows => [...rows, { materialId: '', lotId: '', quantity: '' }]);
  }

  protected selectMaterial(row: DetailInput, materialId: string): void {
    row.materialId = materialId;
    row.lotId = '';
  }

  protected removeRow(index: number): void {
    this.rows.update(rows => rows.filter((_, rowIndex) => rowIndex !== index));
  }

  protected async save(): Promise<void> {
    if (this.busy()) return;
    const operationDate = this.operationDate();
    if (!operationDate || operationDate > this.today) {
      this.message.set('請確認施肥日期。');
      return;
    }
    const details = [];
    for (const row of this.rows()) {
      const quantity = parseOperationDecimal(row.quantity);
      if (!this.availableLotIds().has(row.lotId) || quantity === null || quantity <= 0) {
        this.message.set('請選擇可用的肥料批次。用量須大於零、小於 10 億，且最多六位小數。');
        return;
      }
      details.push({ id: row.id, materialPurchaseLotId: row.lotId, quantity });
    }
    const selected = this.selected();
    const ids = this.ids();
    this.busy.set(true);
    this.message.set('');
    try {
      await firstValueFrom((selected
        ? this.plantings.editFertilization({ ...ids, fertilizationId: selected.id, editFertilizationRqDto: { operationDate, details } })
        : this.plantings.createFertilization({ ...ids, createFertilizationRqDto: { operationDate, details } }))
        .pipe(takeUntilDestroyed(this.destroyRef)));
      if (ids !== this.ids()) return;
      this.editorOpen.set(false);
      await this.load();
    } catch (error) {
      if (ids === this.ids()) this.message.set(operationError(error, '無法儲存施肥紀錄，請再試一次。'));
    } finally {
      this.busy.set(false);
    }
  }
}

