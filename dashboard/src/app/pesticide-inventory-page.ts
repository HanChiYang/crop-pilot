import { DecimalPipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, Observable } from 'rxjs';
import { MaterialsService } from './generated/api/materials.service';
import { PesticideLotRsDto } from './generated/model/pesticideLotRsDto';
import { PesticideMaterialRsDto } from './generated/model/pesticideMaterialRsDto';
import { PesticideStockUnit } from './generated/model/pesticideStockUnit';
import { operationError, parseOperationDecimal, taipeiToday } from './operation-form';
import { pesticideUnits } from './pesticide-units';

type Editor = 'material' | PesticideLotRsDto.SourceEnum;

@Component({
  selector: 'app-pesticide-inventory-page',
  imports: [DecimalPipe, FormsModule],
  styles: `
    .stock-section { padding: 1.25rem; margin-top: 1rem; }
    .stock-section .field-table { margin-top: 1rem; overflow-x: auto; }
    .stock-section .field-table tbody th { width: auto; }
    .actions { display: flex; gap: .5rem; flex-wrap: wrap; }
  `,
  template: `
    <header class="page-heading">
      <h1>農藥庫存</h1>
      @if (!loading() && !loadError() && !editor()) {
        <button class="button" type="button" (click)="openEditor('material')">新增農藥</button>
      }
    </header>
    @if (loading()) {
      <p class="panel empty-state" role="status">正在載入農藥庫存…</p>
    } @else if (loadError()) {
      <section class="panel empty-state">
        <p class="error" role="alert">{{ loadError() }}</p>
        <button class="button button-outline" type="button" (click)="load()">重新載入</button>
      </section>
    } @else if (editor(); as form) {
      <form class="panel field-form" (ngSubmit)="save(form)">
        <fieldset [disabled]="busy()">
          @if (form === 'material') {
            <h2>新增農藥主檔</h2>
            <div class="field-form-grid">
              <div class="field-control">
                <label for="pesticide-name">農藥名稱</label>
                <input id="pesticide-name" name="name" required [(ngModel)]="name">
              </div>
              <div class="field-control">
                <label for="pesticide-unit">庫存單位</label>
                <select id="pesticide-unit" name="stockUnit" required [(ngModel)]="stockUnit">
                  <option value="">請選擇單位</option>
                  @for (unit of units; track unit) {
                    <option [value]="unit">{{ unitLabels[unit] }}</option>
                  }
                </select>
              </div>
            </div>
          } @else if (selectedMaterial(); as material) {
            <h2>{{ form === 'purchase' ? '登記進貨' : '登記期初庫存' }} · {{ material.name }}</h2>
            <div class="field-form-grid">
              @if (form === 'openingStock') {
                <div class="field-control">
                  <label for="pesticide-count-date">盤點日期</label>
                  <input id="pesticide-count-date" name="availableFrom" type="date" [max]="today"
                    required [(ngModel)]="availableFrom">
                </div>
              }
              <div class="field-control">
                <label for="pesticide-purchase-date">購買日期{{ form === 'openingStock' ? '（選填）' : '' }}</label>
                <input id="pesticide-purchase-date" name="purchasedOn" type="date" [max]="today"
                  [required]="form === 'purchase'" [(ngModel)]="purchasedOn">
              </div>
              <div class="field-control">
                <label for="pesticide-quantity">{{ form === 'purchase' ? '進貨量' : '盤點剩餘量' }}（{{ unitLabels[material.stockUnit] }}）</label>
                <input id="pesticide-quantity" name="receivedQuantity" inputmode="decimal" required [(ngModel)]="receivedQuantity">
              </div>
              <div class="field-control">
                <label for="pesticide-price">單價（TWD{{ form === 'openingStock' ? '，選填' : '' }}）</label>
                <input id="pesticide-price" name="unitPrice" inputmode="decimal" [required]="form === 'purchase'" [(ngModel)]="unitPrice">
              </div>
            </div>
          }
          @if (message()) { <p class="error" role="alert">{{ message() }}</p> }
          <div class="field-form-actions">
            <button class="button button-outline" type="button" (click)="editor.set(null)">取消</button>
            <button class="button" type="submit">儲存</button>
          </div>
        </fieldset>
      </form>
    } @else if (!materials().length) {
      <p class="panel empty-state">目前沒有農藥主檔。</p>
    } @else {
      <section class="panel field-table" aria-label="農藥主檔清單">
        <table>
          <thead><tr><th scope="col">農藥名稱</th><th scope="col">庫存單位</th><th scope="col">總餘量</th><th scope="col">操作</th></tr></thead>
          <tbody>
            @for (material of materials(); track material.id) {
              <tr>
                <th scope="row">{{ material.name }}</th>
                <td>{{ unitLabels[material.stockUnit] }}</td>
                <td>{{ material.remainingQuantity | number:'1.0-6' }}</td>
                <td><button class="button button-outline" type="button" (click)="loadLots(material.id)"
                  [attr.aria-pressed]="selectedId() === material.id">查看批次</button></td>
              </tr>
            }
          </tbody>
        </table>
      </section>
      @if (selectedMaterial(); as material) {
        <section class="panel stock-section" aria-labelledby="pesticide-lots">
          <header class="page-heading">
            <h2 id="pesticide-lots">{{ material.name }}的庫存批次</h2>
            <div class="actions">
              <button class="button button-outline" type="button" (click)="openEditor('purchase')">登記進貨</button>
              <button class="button" type="button" (click)="openEditor('openingStock')">登記期初庫存</button>
            </div>
          </header>
          @if (lotsLoading()) {
            <p role="status">正在載入庫存批次…</p>
          } @else if (lotError()) {
            <p class="error" role="alert">{{ lotError() }}</p>
            <button class="button button-outline" type="button" (click)="loadLots(material.id)">重新載入批次</button>
          } @else if (!lots().length) {
            <p>此農藥尚無庫存批次。</p>
          } @else {
            <div class="field-table">
              <table>
                <thead><tr><th scope="col">來源</th><th scope="col">生效日</th><th scope="col">購買日</th>
                  <th scope="col">登記量</th><th scope="col">剩餘量</th><th scope="col">單價</th><th scope="col">整批金額</th></tr></thead>
                <tbody>
                  @for (lot of lots(); track lot.id) {
                    <tr>
                      <th scope="row">{{ lot.source === 'purchase' ? '進貨' : '期初庫存' }}</th>
                      <td>{{ lot.availableFrom }}</td><td>{{ lot.purchasedOn || '—' }}</td>
                      <td>{{ lot.receivedQuantity | number:'1.0-6' }} {{ unitLabels[material.stockUnit] }}</td>
                      <td>{{ lot.remainingQuantity | number:'1.0-6' }} {{ unitLabels[material.stockUnit] }}</td>
                      <td>{{ lot.unitPrice === null ? '未填' : ('NT$' + (lot.unitPrice | number:'1.0-6')) }}</td>
                      <td>{{ lot.totalCost === null ? '未填' : ('NT$' + (lot.totalCost | number:'1.0-12')) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }
    }
  `,
})
export class PesticideInventoryPage {
  private readonly api = inject(MaterialsService);
  private readonly destroyRef = inject(DestroyRef);
  private lotRequest = 0;

  protected today = taipeiToday();
  protected readonly units = Object.values(PesticideStockUnit);
  protected readonly unitLabels = pesticideUnits;
  protected readonly materials = signal<PesticideMaterialRsDto[]>([]);
  protected readonly lots = signal<PesticideLotRsDto[]>([]);
  protected readonly selectedId = signal('');
  protected readonly selectedMaterial = computed(() => this.materials().find(material => material.id === this.selectedId()));
  protected readonly loading = signal(true);
  protected readonly lotsLoading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal('');
  protected readonly lotError = signal('');
  protected readonly message = signal('');
  protected readonly editor = signal<Editor | null>(null);

  protected name = '';
  protected stockUnit: PesticideStockUnit | '' = '';
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
      this.materials.set(await this.read(this.api.listPesticideMaterials()));
      if (this.selectedMaterial()) await this.loadLots(this.selectedId());
    } catch {
      this.loadError.set('無法載入農藥庫存。');
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
      const lots = await this.read(this.api.listPesticideLots({ materialId }));
      if (request === this.lotRequest) this.lots.set(lots);
    } catch {
      if (request === this.lotRequest) this.lotError.set('無法載入庫存批次。');
    } finally {
      if (request === this.lotRequest) this.lotsLoading.set(false);
    }
  }

  protected openEditor(form: Editor): void {
    this.today = taipeiToday();
    this.name = '';
    this.stockUnit = '';
    this.purchasedOn = '';
    this.availableFrom = this.today;
    this.receivedQuantity = '';
    this.unitPrice = '';
    this.message.set('');
    this.editor.set(form);
  }

  protected async save(form: Editor): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    try {
      if (form === 'material') {
        const name = this.name.trim();
        if (!name || !this.stockUnit) {
          this.message.set('請填寫農藥名稱並選擇庫存單位。');
          return;
        }
        if (Array.from(name).length > 100) {
          this.message.set('農藥名稱最多 100 字。');
          return;
        }
        const material = await this.read(this.api.createPesticideMaterial({
          createPesticideMaterialRqDto: { name, stockUnit: this.stockUnit },
        }));
        this.selectedId.set(material.id);
      } else {
        const receivedQuantity = parseOperationDecimal(this.receivedQuantity);
        const unitPrice = parseOperationDecimal(this.unitPrice);
        if (receivedQuantity === null || receivedQuantity <= 0 || (unitPrice === null && this.unitPrice.trim())) {
          this.message.set('數量須大於零，單價可為零。兩者須小於 10 億，且最多六位小數。');
          return;
        }
        if (form === 'purchase' && (!this.purchasedOn || unitPrice === null)) {
          this.message.set('進貨須填寫購買日期及單價。');
          return;
        }
        if (form === 'openingStock' && !this.availableFrom) {
          this.message.set('請填寫盤點日期。');
          return;
        }
        await this.read(this.api.createPesticideLot({
          materialId: this.selectedId(),
          createPesticideLotRqDto: {
            source: form,
            receivedQuantity,
            unitPrice,
            purchasedOn: this.purchasedOn || null,
            availableFrom: form === 'openingStock' ? this.availableFrom : undefined,
          },
        }));
      }
      this.editor.set(null);
      await this.load();
    } catch (error) {
      this.message.set(operationError(error, '無法儲存庫存資料，請再試一次。'));
    } finally {
      this.busy.set(false);
    }
  }

  private read<T>(request: Observable<T>): Promise<T> {
    return firstValueFrom(request.pipe(takeUntilDestroyed(this.destroyRef)));
  }
}
