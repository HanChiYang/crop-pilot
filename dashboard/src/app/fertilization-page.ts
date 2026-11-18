import { DecimalPipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, of, switchMap } from 'rxjs';
import { FieldsService } from './generated/api/fields.service';
import { MaterialsService } from './generated/api/materials.service';
import { PlantingsService } from './generated/api/plantings.service';
import { MaterialApplicationRsDto } from './generated/model/materialApplicationRsDto';
import { FertilizerLotRsDto } from './generated/model/fertilizerLotRsDto';
import { FertilizerMaterialRsDto } from './generated/model/fertilizerMaterialRsDto';
import { MaterialApplicationMethod } from './generated/model/materialApplicationMethod';
import { PesticideLotRsDto } from './generated/model/pesticideLotRsDto';
import { materialPackageLabel, pesticideUnits } from './pesticide-units';
import { HttpErrorResponse } from '@angular/common/http';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { compareOperationValues, nextOperationSort, OperationSort, operationError, parseOperationDecimal, latestRecordDate, latestRecordLabel, taipeiToday } from './operation-form';
import { plantingStatusLabel } from './planting-display';

type MaterialType = 'fertilizer' | 'pesticide';
type MaterialSort = 'operationDate' | 'sprayWaterVolumeLiters';
const methods = { spray: '噴施', broadcast: '撒施' } as const;

interface DetailInput {
  materialType: MaterialType;
  id?: string;
  original?: { lotId: string };
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
    .detail { display: grid; grid-template-columns: minmax(6rem, 1fr) minmax(8rem, 1.5fr) minmax(10rem, 2fr) minmax(6rem, 1fr) auto; gap: .75rem; align-items: end; }
    .material-usage { padding: 0; margin: 0; list-style: none; }
    .material-usage small { color: var(--muted); }
    .material-usage li + li { margin-top: .25rem; }
    .material-detail-heading { align-items: center; justify-content: space-between; margin-top: 1rem; }
    .material-detail-heading h2 { margin: 0; font-size: 1.15rem; }
    .material-search { max-width: 20rem; margin-bottom: 1rem; }
    .method-choices { display: flex; gap: 1.1rem; margin-block: .4rem 1rem; }
    .method-choices label { display: inline-flex; gap: .35rem; align-items: center; }
    .method-choices input { width: auto; }
    .delete-material-items { padding-left: 1.2rem; }
    @media (max-width: 650px) { .detail { grid-template-columns: 1fr; } }
  `,
  template: `
    <header class="page-heading">
      <div>
        <h1>{{ editorOpen() ? (selected() ? '編輯資材施用' : '新增資材施用') : '資材施用紀錄' }}</h1>
        @if (planting(); as item) { <p>{{ fieldName() }}・{{ item.cropName }}・{{ statusLabel[item.status] }}</p> }
      </div>
      <div class="actions">
        <a class="button button-outline" [routerLink]="backLink()" [queryParams]="backQuery">返回{{ fromHistory ? '栽培詳情' : '作物栽培管理' }}</a>
        <a class="button button-outline" routerLink="/dashboard/materials">查看資材庫存</a>
        @if (!loading() && !loadError() && planting() && !editorOpen()) {
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
        <fieldset [disabled]="busy()">
          <div class="field-control">
            <label for="operation-date">施用日期</label>
            <input id="operation-date" type="date" name="operationDate" required [max]="latestDate()"
              [ngModel]="operationDate()" (ngModelChange)="operationDate.set($event)">
          </div>
          <fieldset class="method-choices" [disabled]="busy()">
            <legend>施用方式</legend>
            <label><input type="radio" name="applicationMethod" value="spray" required
              [ngModel]="applicationMethod()" (ngModelChange)="selectMethod($event)"> 噴施</label>
            <label><input type="radio" name="applicationMethod" value="broadcast" required
              [ngModel]="applicationMethod()" (ngModelChange)="selectMethod($event)"> 撒施</label>
          </fieldset>
          @if (hasSpray()) {
            <div class="field-control">
              <label for="spray-water-volume">噴施總水量（公升）</label>
              <input id="spray-water-volume" name="sprayWaterVolumeLiters" inputmode="decimal"
                [ngModel]="sprayWater()" (ngModelChange)="sprayWater.set($event)">
            </div>
          }
          <div class="actions material-detail-heading">
            <h2>資材明細</h2>
            <button class="button button-outline" type="button" (click)="addRow()">＋ 新增資材</button>
          </div>
          @if (!availableLots().length) {
            <p>該日沒有可用批次。<a routerLink="/dashboard/materials">前往資材管理</a></p>
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
                      <option [value]="material.id">{{ material.name }}{{ material.package ? ' · ' + packageLabel(material.package) : '' }}</option>
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
                      <option [value]="lot.id">{{ lot.label }}｜可用 {{ lot.available | number:'1.0-6' }} {{ lot.unit }}｜批次單價 {{ lot.unitPrice == null ? '成本待補' : 'NT$' + lot.unitPrice }}</option>
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
                  @if (unitFor(row.lotId); as unit) { <span>{{ unit }}</span> }
                  @if (detailErrors()[index]; as error) { <small class="error" role="alert">{{ error }}</small> }
                </div>
                <button class="button button-outline" type="button" [disabled]="rows().length === 1"
                  [attr.aria-label]="'移除第 ' + (index + 1) + ' 項資材'" (click)="removeRow(index)">移除</button>
              </div>
            }
          </div>
          <div class="field-control">
            <label for="material-description">備註（選填）</label>
            <textarea id="material-description" name="description" rows="2"
              [ngModel]="description()" (ngModelChange)="description.set($event)"></textarea>
          </div>
          @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
          <div class="field-form-actions">
            <button class="button button-outline" type="button" (click)="editorOpen.set(false)">取消</button>
            <button class="button" type="submit">儲存</button>
          </div>
        </fieldset>
      </form>
    } @else if (!operations().length) {
      <section class="panel empty-state">
        <strong>尚無資材施用紀錄</strong>
        <p>可在同一筆紀錄登記農藥和肥料。</p>
      </section>
    } @else {
      <div class="field-control material-search">
        <label for="material-search">關鍵字搜尋</label>
        <input id="material-search" type="search" placeholder="搜尋資材名稱"
          [ngModel]="keyword()" (ngModelChange)="keyword.set($event)">
      </div>
      <div class="panel field-table">
        <table aria-label="資材施用紀錄清單">
          <thead><tr>
            <th scope="col" [attr.aria-sort]="sort().column === 'operationDate' ? sort().direction : null">
              <button class="operation-sort" type="button" (click)="sortBy('operationDate')">施用日期 <span aria-hidden="true">{{ sortArrow('operationDate') }}</span></button>
            </th>
            <th scope="col">資材明細</th>
            <th scope="col" [attr.aria-sort]="sort().column === 'sprayWaterVolumeLiters' ? sort().direction : null">
              <button class="operation-sort" type="button" (click)="sortBy('sprayWaterVolumeLiters')">噴施總水量 <span aria-hidden="true">{{ sortArrow('sprayWaterVolumeLiters') }}</span></button>
            </th>
            <th scope="col">操作</th>
          </tr></thead>
          <tbody>
            @for (operation of visibleOperations(); track operation.id) {
              <tr>
                <td><time [attr.datetime]="operation.operationDate">{{ operation.operationDate }}</time></td>
                <td><ul class="material-usage">
                  @for (detail of operation.details; track detail.id) {
                    <li>
                      {{ detail.materialName }} {{ detail.quantity | number:'1.0-6' }} {{ detail.unit }}
                      @if (detail.unitPrice === null) { <small>・成本待補</small> }
                    </li>
                  }
                </ul></td>
                <td>
                  @if (operation.sprayWaterVolumeLiters != null) {
                    {{ operation.sprayWaterVolumeLiters | number:'1.0-6' }} 公升
                  } @else {
                    {{ operation.applicationMethod === 'spray' ? '未記錄' : '—' }}
                  }
                </td>
                <td>
                  <button class="button button-outline row-action-trigger" type="button" [disabled]="busy()"
                    [attr.popovertarget]="'material-actions-' + operation.id"
                    [attr.aria-label]="'操作 ' + operation.operationDate + ' 的資材施用'">☰</button>
                  <div #menu popover class="row-action-menu" [id]="'material-actions-' + operation.id">
                    <button type="button" (click)="menu.hidePopover(); open(operation)">編輯</button>
                    <button class="row-action-danger" type="button"
                      (click)="menu.hidePopover(); confirmDelete(operation, deleteDialog)">刪除</button>
                  </div>
                </td>
              </tr>
            } @empty {
              <tr><td colspan="4" class="empty-state">查無符合的資材施用紀錄。</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
    <dialog #deleteDialog class="field-delete-dialog" aria-labelledby="material-delete-title"
      (cancel)="busy() && $event.preventDefault()">
      <h2 id="material-delete-title">刪除資材施用紀錄？</h2>
      @if (pendingDelete(); as operation) {
        @if (planting(); as item) { <p>{{ fieldName() }}・{{ item.cropName }}・{{ statusLabel[item.status] }}</p> }
        <p>{{ operation.operationDate }}・{{ methodLabels[operation.applicationMethod] }}
          @if (operation.sprayWaterVolumeLiters != null) {
            ・噴施總水量 {{ operation.sprayWaterVolumeLiters | number:'1.0-6' }} 公升
          }
        </p>
        <ul class="delete-material-items">
          @for (detail of operation.details; track detail.id) {
            <li>{{ detail.materialName }} {{ detail.quantity | number:'1.0-6' }} {{ detail.unit }}</li>
          }
        </ul>
        <p>刪除後，各項用量將退回原庫存批次。</p>
        @if (deleteError()) { <p class="error" role="alert">{{ deleteError() }}</p> }
        <div class="field-form-actions">
          <button class="button button-outline" type="button" autofocus [disabled]="busy()" (click)="deleteDialog.close()">取消</button>
          <button class="button button-danger" type="button" [disabled]="busy()" (click)="delete(operation.id, deleteDialog)">
            {{ busy() ? '刪除中…' : '刪除' }}
          </button>
        </div>
      }
    </dialog>
  `,
})
export class FertilizationPage {
  private readonly route = inject(ActivatedRoute);
  private readonly plantings = inject(PlantingsService);
  private readonly fields = inject(FieldsService);
  private readonly materialsApi = inject(MaterialsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ids = signal({ fieldId: '', plantingId: '' });
  private requestVersion = 0;
  private readonly inventory = signal<{ material: FertilizerMaterialRsDto; type: MaterialType; lots: (FertilizerLotRsDto | PesticideLotRsDto)[] }[]>([]);

  protected readonly fromHistory = this.route.snapshot.data['fromHistory'] === true;
  protected readonly packageLabel = materialPackageLabel;
  protected readonly backLink = computed(() => this.fromHistory
    ? ['/dashboard/planting-history', this.ids().fieldId, this.ids().plantingId]
    : ['/dashboard/plantings']);
  protected readonly backQuery = this.fromHistory ? {} : { fieldId: this.route.snapshot.queryParamMap.get('fieldId') };
  protected readonly planting = signal<PlantingRsDto | null>(null);
  protected readonly operations = signal<MaterialApplicationRsDto[]>([]);
  protected readonly fieldName = signal('');
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly methodLabels = methods;
  protected readonly keyword = signal('');
  protected readonly sort = signal<OperationSort<MaterialSort>>({ column: 'operationDate', direction: 'descending' });
  protected readonly visibleOperations = computed(() => {
    const keyword = this.keyword().trim().toLocaleLowerCase('zh-Hant-TW');
    const { column, direction } = this.sort();
    return this.operations().filter(operation =>
      operation.details.some(detail => detail.materialName.toLocaleLowerCase('zh-Hant-TW').includes(keyword)))
      .sort((a, b) => compareOperationValues(a[column], b[column], direction));
  });
  protected readonly today = signal(taipeiToday());
  protected readonly latestDate = computed(() => latestRecordDate(this.planting()?.completionDate, this.today()));
  protected readonly operationDate = signal(this.today());
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly editorOpen = signal(false);
  protected readonly loadError = signal('');
  protected readonly message = signal('');
  protected readonly selected = signal<MaterialApplicationRsDto | null>(null);
  protected readonly rows = signal<DetailInput[]>([]);
  protected readonly sprayWater = signal('');
  protected readonly detailErrors = signal<Record<number, string>>({});
  protected readonly pendingDelete = signal<MaterialApplicationRsDto | null>(null);
  protected readonly deleteError = signal('');
  protected readonly applicationMethod = signal<MaterialApplicationMethod | ''>('');
  protected readonly description = signal('');
  protected readonly hasSpray = computed(() => this.applicationMethod() === 'spray');

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
      this.keyword.set('');
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
        field: this.fields.getField({ fieldId: ids.fieldId }),
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
      this.fieldName.set(data.field.name);
      this.operations.set(data.operations);
      this.inventory.set([...data.inventory, ...data.pesticides]);
    } catch {
      if (version === this.requestVersion) this.loadError.set('無法載入資材施用紀錄及庫存。');
    } finally {
      if (version === this.requestVersion) this.loading.set(false);
    }
  }

  protected sortBy(column: MaterialSort): void {
    this.sort.update(current => nextOperationSort(current, column));
  }

  protected sortArrow(column: MaterialSort): string {
    const sort = this.sort();
    return sort.column !== column ? '↕' : sort.direction === 'ascending' ? '↑' : '↓';
  }

  protected open(operation?: MaterialApplicationRsDto): void {
    this.today.set(taipeiToday());
    this.applicationMethod.set(operation?.applicationMethod ?? '');
    this.description.set(operation?.description ?? '');
    this.selected.set(operation ?? null);
    this.detailErrors.set({});
    this.pendingDelete.set(null);
    this.sprayWater.set(operation?.sprayWaterVolumeLiters == null ? '' : String(operation.sprayWaterVolumeLiters));
    this.operationDate.set(operation?.operationDate ?? this.latestDate());
    this.rows.set(operation?.details.map(detail => ({
      id: detail.id, materialType: detail.materialType, materialId: detail.materialId, lotId: detail.materialPurchaseLotId, quantity: String(detail.quantity),
      original: { lotId: detail.materialPurchaseLotId },
    })) ?? [{ materialType: 'fertilizer', materialId: '', lotId: '', quantity: '' }]);
    this.message.set('');
    this.editorOpen.set(true);
  }

  protected addRow(): void {
    this.rows.update(rows => [...rows, { materialType: 'fertilizer', materialId: '', lotId: '', quantity: '' }]);
  }

  protected selectType(row: DetailInput, type: MaterialType): void {
    row.materialType = type;
    this.selectMaterial(row, '');
  }

  protected selectMethod(method: MaterialApplicationMethod): void {
    this.applicationMethod.set(method);
    if (!this.hasSpray()) this.sprayWater.set('');
  }

  protected unitFor(lotId: string): string {
    return this.availableLots().find(lot => lot.id === lotId)?.unit ?? '';
  }

  protected selectMaterial(row: DetailInput, materialId: string): void {
    row.materialId = materialId;
    row.lotId = '';
  }

  protected removeRow(index: number): void {
    this.rows.update(rows => rows.filter((_, rowIndex) => rowIndex !== index));
    this.detailErrors.set({});
  }

  protected async save(): Promise<void> {
    if (this.busy()) return;
    this.today.set(taipeiToday());
    const operationDate = this.operationDate();
    const applicationMethod = this.applicationMethod();
    if (!operationDate || operationDate > this.latestDate() || !applicationMethod) {
      this.message.set(`請確認施用日期及施用方式，日期最晚為${latestRecordLabel(this.planting()?.completionDate, this.today())}。`);
      return;
    }
    this.detailErrors.set({});
    const sprayWaterVolumeLiters = this.sprayWater().trim() ? parseOperationDecimal(this.sprayWater()) : null;
    if (this.sprayWater().trim() && (sprayWaterVolumeLiters === null || sprayWaterVolumeLiters <= 0)) {
      this.message.set('噴施總水量須大於零、小於 10 億，且最多六位小數。');
      return;
    }
    const description = this.description().trim();
    if ([...description].length > 500 || description.includes('\0')) {
      this.message.set('施用備註最多 500 字，且不可包含空字元。');
      return;
    }
    const details = [];
    for (const row of this.rows()) {
      const quantity = parseOperationDecimal(row.quantity);
      if (!this.availableLotIds().has(row.lotId) || quantity === null || quantity <= 0) {
        this.message.set('請選擇可用的庫存批次。用量須大於零、小於 10 億，且最多六位小數。');
        return;
      }
      details.push({ id: row.id, materialPurchaseLotId: row.lotId, quantity });
    }
    const selected = this.selected();
    const ids = this.ids();
    const request = { operationDate, applicationMethod, sprayWaterVolumeLiters, description: description || null, details };
    this.busy.set(true);
    this.message.set('');
    try {
      await firstValueFrom((selected
        ? this.plantings.editMaterialApplication({ materialApplicationId: selected.id, editMaterialApplicationRqDto: request })
        : this.plantings.createMaterialApplication({ plantingId: ids.plantingId, createMaterialApplicationRqDto: request }))
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
  protected confirmDelete(operation: MaterialApplicationRsDto, dialog: HTMLDialogElement): void {
    this.pendingDelete.set(operation);
    this.deleteError.set('');
    dialog.showModal();
  }

  protected async delete(id: string, dialog: HTMLDialogElement): Promise<void> {
    if (this.busy()) return;
    const ids = this.ids();
    this.busy.set(true);
    this.deleteError.set('');
    try {
      await firstValueFrom(this.plantings.deleteMaterialApplication({ materialApplicationId: id })
        .pipe(takeUntilDestroyed(this.destroyRef)));
      if (ids !== this.ids()) return;
      dialog.close();
      this.pendingDelete.set(null);
      await this.load();
    } catch (error) {
      if (ids === this.ids()) this.deleteError.set(operationError(error, '無法刪除資材施用紀錄，請再試一次。'));
    } finally {
      this.busy.set(false);
    }
  }
}
