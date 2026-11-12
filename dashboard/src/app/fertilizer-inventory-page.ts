import { DecimalPipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, Observable } from 'rxjs';
import { MaterialsService } from './generated/api/materials.service';
import { FertilizerLotRsDto } from './generated/model/fertilizerLotRsDto';
import { FertilizerMaterialRsDto } from './generated/model/fertilizerMaterialRsDto';
import { operationError, parseOperationDecimal, taipeiToday } from './operation-form';

type Editor = { kind: 'material'; id?: string } |
  { kind: 'lot'; materialId: string; id?: string };
type Deletion = { label: string; materialId: string; lotId?: string };

@Component({
  selector: 'app-fertilizer-inventory-page',
  imports: [DecimalPipe, FormsModule],
  styles: `
    .inventory { display: grid; gap: 1rem; }
    .actions { display: flex; gap: .5rem; flex-wrap: wrap; }
    .stock-section { padding: 1.25rem; }
    .stock-section .field-table { margin-top: 1rem; }
    .stock-section .field-table tbody th { width: auto; }
  `,
  template: `
    <header class="page-heading">
      <h1>肥料庫存</h1>
      <button class="button" type="button" [disabled]="busy() || loading() || !!editor() || !!deletion()"
        (click)="openMaterial()">新增肥料資材</button>
    </header>
    @if (loading()) {
      <p class="panel empty-state" role="status">正在載入肥料庫存…</p>
    } @else if (loadError()) {
      <section class="panel empty-state">
        <p class="error" role="alert">{{ loadError() }}</p>
        <button class="button button-outline" type="button" (click)="load()">重新載入</button>
      </section>
    } @else if (editor(); as form) {
      <form class="panel field-form" (ngSubmit)="save(form)">
        <fieldset [disabled]="busy()">
          @if (form.kind === 'material') {
            <h2>{{ form.id ? '編輯肥料資材' : '新增肥料資材' }}</h2>
            <div class="field-form-grid">
              <div class="field-control"><label for="material-name">肥料名稱</label>
                <input id="material-name" name="name" maxlength="100" required [(ngModel)]="name"></div>
              <div class="field-control"><label for="stock-unit">庫存單位</label>
                <input id="stock-unit" name="unit" maxlength="20" required [(ngModel)]="stockUnit" [readonly]="unitLocked">
                @if (unitLocked) { <small>已有庫存批次，單位固定。</small> }
              </div>
            </div>
          } @else {
            <h2>{{ form.id ? '編輯批次' : '新增批次' }} · {{ selectedMaterial()?.name }}</h2>
            <div class="field-form-grid">
              <div class="field-control"><label for="lot-source">批次來源</label>
                <select id="lot-source" name="source" [(ngModel)]="source" [disabled]="!!form.id">
                  <option value="purchase">進貨</option><option value="openingStock">期初庫存</option>
                </select>
              </div>
              <div class="field-control"><label for="purchase-date">購買日期{{ source === 'purchase' ? '' : '（選填）' }}</label>
                <input id="purchase-date" name="purchasedOn" type="date" [max]="today" [required]="source === 'purchase'" [(ngModel)]="purchasedOn"></div>
              @if (source === 'openingStock') {
                <div class="field-control"><label for="available-date">盤點日期</label>
                  <input id="available-date" name="availableFrom" type="date" [max]="today" [(ngModel)]="availableFrom"></div>
              }
              <div class="field-control"><label for="received-quantity">{{ source === 'purchase' ? '進貨量' : '盤點剩餘量' }}（{{ selectedMaterial()?.stockUnit }}）</label>
                <input id="received-quantity" name="receivedQuantity" inputmode="decimal" required [(ngModel)]="receivedQuantity"></div>
              <div class="field-control"><label for="unit-price">單價（TWD，選填）</label>
                <input id="unit-price" name="unitPrice" inputmode="decimal" [(ngModel)]="unitPrice"></div>
            </div>
          }
          @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
          <div class="field-form-actions">
            <button class="button button-outline" type="button" (click)="closeEditor()">取消</button>
            <button class="button" type="submit">儲存</button>
          </div>
        </fieldset>
      </form>
    } @else if (deletion(); as target) {
      <section class="panel field-form" aria-labelledby="inventory-delete-title">
        <h2 id="inventory-delete-title">確認刪除</h2>
        <p>要刪除「{{ target.label }}」嗎？</p>
        @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
        <div class="field-form-actions">
          <button class="button button-outline" type="button" [disabled]="busy()" (click)="deletion.set(null)">取消</button>
          <button class="button" type="button" [disabled]="busy()" (click)="remove(target)">刪除</button>
        </div>
      </section>
    } @else if (!materials().length) {
      <p class="panel empty-state">目前沒有肥料資材。</p>
    } @else {
      <div class="inventory">
        <section class="panel field-table" aria-label="肥料資材清單">
          <table>
            <thead><tr><th scope="col">肥料名稱</th><th scope="col">總餘量</th><th scope="col">操作</th></tr></thead>
            <tbody>
              @for (material of materials(); track material.id) {
                <tr>
                  <th scope="row">{{ material.name }}</th>
                  <td>{{ material.remainingQuantity | number:'1.0-6' }} {{ material.stockUnit }}</td>
                  <td><button class="button button-outline" type="button" (click)="loadLots(material.id)"
                    [attr.aria-pressed]="selectedId() === material.id">查看批次</button></td>
                </tr>
              }
            </tbody>
          </table>
        </section>
        @if (selectedMaterial(); as material) {
          <section class="panel stock-section" aria-labelledby="lot-heading">
            <header class="page-heading">
              <h2 id="lot-heading">{{ material.name }}的庫存批次</h2>
              <div class="actions">
                <button class="button button-outline" type="button" [disabled]="lotsLoading() || !!lotError()" (click)="openMaterial(material)">編輯資材</button>
                <button class="button button-outline" type="button" [disabled]="lotsLoading() || !!lotError() || lots().length > 0"
                  (click)="confirmDelete(material)">刪除資材</button>
                <button class="button" type="button" [disabled]="lotsLoading()" (click)="openLot(material)">新增批次</button>
              </div>
            </header>
            @if (lotsLoading()) {
              <p role="status">正在載入庫存批次…</p>
            } @else if (lotError()) {
              <p class="error" role="alert">{{ lotError() }}</p>
              <button class="button button-outline" type="button" (click)="loadLots(material.id)">重新載入批次</button>
            } @else if (!lots().length) {
              <p>此資材尚無庫存批次。</p>
            } @else {
              <div class="field-table"><table>
                <thead><tr><th scope="col">來源</th><th scope="col">生效日</th><th scope="col">購買日</th><th scope="col">登記量</th><th scope="col">剩餘量</th><th scope="col">單價</th><th scope="col">操作</th></tr></thead>
                <tbody>
                  @for (lot of lots(); track lot.id) {
                    <tr>
                      <th scope="row">{{ lot.source === 'purchase' ? '進貨' : '期初庫存' }}</th>
                      <td>{{ lot.availableFrom }}</td><td>{{ lot.purchasedOn || '—' }}</td>
                      <td>{{ lot.receivedQuantity | number:'1.0-6' }} {{ material.stockUnit }}</td>
                      <td>{{ lot.remainingQuantity | number:'1.0-6' }} {{ material.stockUnit }}</td>
                      <td>{{ lot.unitPrice === null ? '成本待補' : ('NT$' + (lot.unitPrice | number:'1.0-6')) }}</td>
                      <td class="actions">
                        <button class="button button-outline" type="button" (click)="openLot(material, lot)">編輯</button>
                        <button class="button button-outline" type="button" [disabled]="lot.receivedQuantity !== lot.remainingQuantity"
                          (click)="confirmDelete(material, lot)">刪除</button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table></div>
            }
          </section>
        }
      </div>
    }
  `,
})
export class FertilizerInventoryPage {
  private readonly api = inject(MaterialsService);
  private readonly destroyRef = inject(DestroyRef);
  private lotRequest = 0;

  protected readonly today = taipeiToday();
  protected readonly materials = signal<FertilizerMaterialRsDto[]>([]);
  protected readonly lots = signal<FertilizerLotRsDto[]>([]);
  protected readonly selectedId = signal('');
  protected readonly selectedMaterial = computed(() => this.materials().find(material => material.id === this.selectedId()));
  protected readonly loading = signal(true);
  protected readonly lotsLoading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal('');
  protected readonly lotError = signal('');
  protected readonly message = signal('');
  protected readonly editor = signal<Editor | null>(null);
  protected readonly deletion = signal<Deletion | null>(null);

  protected name = '';
  protected stockUnit = '';
  protected unitLocked = false;
  protected source: FertilizerLotRsDto.SourceEnum = 'purchase';
  protected purchasedOn = '';
  protected availableFrom = '';
  protected receivedQuantity = '';
  protected unitPrice = '';

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set('');
    try {
      this.materials.set(await this.read(this.api.listFertilizerMaterials()));
      if (this.selectedMaterial()) await this.loadLots(this.selectedId());
    } catch {
      this.loadError.set('無法載入肥料庫存。');
    } finally {
      this.loading.set(false);
    }
  }

  protected async loadLots(materialId: string): Promise<void> {
    const request = ++this.lotRequest;
    this.selectedId.set(materialId);
    this.lotsLoading.set(true);
    this.lotError.set('');
    this.lots.set([]);
    try {
      const lots = await this.read(this.api.listFertilizerLots({ materialId }));
      if (request === this.lotRequest) this.lots.set(lots);
    } catch {
      if (request === this.lotRequest) this.lotError.set('無法載入批次。');
    } finally {
      if (request === this.lotRequest) this.lotsLoading.set(false);
    }
  }

  protected openMaterial(material?: FertilizerMaterialRsDto): void {
    this.name = material?.name ?? '';
    this.stockUnit = material?.stockUnit ?? '';
    this.unitLocked = !!material && this.lots().length > 0;
    this.message.set('');
    this.editor.set({ kind: 'material', id: material?.id });
  }

  protected openLot(material: FertilizerMaterialRsDto, lot?: FertilizerLotRsDto): void {
    this.source = lot?.source ?? 'purchase';
    this.purchasedOn = lot?.purchasedOn ?? '';
    this.availableFrom = lot?.availableFrom ?? this.today;
    this.receivedQuantity = lot ? String(lot.receivedQuantity) : '';
    this.unitPrice = lot?.unitPrice == null ? '' : String(lot.unitPrice);
    this.message.set('');
    this.editor.set({ kind: 'lot', materialId: material.id, id: lot?.id });
  }

  protected closeEditor(): void {
    this.editor.set(null);
    this.message.set('');
  }

  protected async save(form: Editor): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    try {
      if (form.kind === 'material') {
        const createFertilizerMaterialRqDto = { name: this.name.trim(), stockUnit: this.stockUnit.trim() };
        if (!createFertilizerMaterialRqDto.name || !createFertilizerMaterialRqDto.stockUnit) {
          this.message.set('請填寫肥料名稱及庫存單位。');
          return;
        }
        const material = await this.read(form.id
          ? this.api.editFertilizerMaterial({ materialId: form.id, createFertilizerMaterialRqDto })
          : this.api.createFertilizerMaterial({ createFertilizerMaterialRqDto }));
        this.selectedId.set(material.id);
      } else {
        const receivedQuantity = parseOperationDecimal(this.receivedQuantity);
        const unitPrice = parseOperationDecimal(this.unitPrice);
        if (receivedQuantity === null || receivedQuantity <= 0 || (unitPrice === null && this.unitPrice.trim())) {
          this.message.set('數量須大於零，單價可為零。兩者須小於 10 億，且最多六位小數。');
          return;
        }
        if (this.source === 'purchase' && !this.purchasedOn) {
          this.message.set('請填寫購買日期。');
          return;
        }
        const lot = {
          receivedQuantity,
          purchasedOn: this.purchasedOn || null,
          availableFrom: this.source === 'openingStock' ? this.availableFrom || undefined : undefined,
          unitPrice,
        };
        await this.read(form.id
          ? this.api.editFertilizerLot({ lotId: form.id, editFertilizerLotRqDto: lot })
          : this.api.createFertilizerLot({ materialId: form.materialId, createFertilizerLotRqDto: { ...lot, source: this.source } }));
      }
      this.closeEditor();
      await this.load();
    } catch (error) {
      this.message.set(operationError(error, '無法儲存庫存資料，請再試一次。'));
    } finally {
      this.busy.set(false);
    }
  }

  protected confirmDelete(material: FertilizerMaterialRsDto, lot?: FertilizerLotRsDto): void {
    this.message.set('');
    this.deletion.set({ materialId: material.id, lotId: lot?.id,
      label: lot ? material.name + ' ' + lot.availableFrom + ' 批次' : material.name });
  }

  protected async remove(target: Deletion): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    try {
      await this.read(target.lotId
        ? this.api.deleteFertilizerLot({ lotId: target.lotId })
        : this.api.deleteFertilizerMaterial({ materialId: target.materialId }));
      if (!target.lotId) this.selectedId.set('');
      this.deletion.set(null);
      await this.load();
    } catch (error) {
      this.message.set(operationError(error, '無法刪除庫存資料，請再試一次。'));
    } finally {
      this.busy.set(false);
    }
  }

  private read<T>(request: Observable<T>): Promise<T> {
    return firstValueFrom(request.pipe(takeUntilDestroyed(this.destroyRef)));
  }
}
