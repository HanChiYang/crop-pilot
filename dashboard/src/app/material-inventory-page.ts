import { DecimalPipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal, WritableSignal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom, from, map, mergeMap, Observable, toArray } from 'rxjs';
import { MaterialsService } from './generated/api/materials.service';
import { FertilizerLotRsDto } from './generated/model/fertilizerLotRsDto';
import { FertilizerMaterialRsDto } from './generated/model/fertilizerMaterialRsDto';
import { MaterialPackageUnit } from './generated/model/materialPackageUnit';
import { PesticideLotRsDto } from './generated/model/pesticideLotRsDto';
import { PesticideStockUnit } from './generated/model/pesticideStockUnit';
import { operationError, parseOperationDecimal, taipeiToday } from './operation-form';
import { materialPackageLabel, pesticideUnits } from './pesticide-units';

type StockLot = FertilizerLotRsDto | PesticideLotRsDto;
type Specification = FertilizerMaterialRsDto & { lots: StockLot[] };
type Editor = { kind: 'spec' } |
  { kind: 'purchase' } |
  { kind: 'lot'; spec: Specification; lot: StockLot };
type Deletion = { spec: Specification; lot?: StockLot };
type SpecColumn = 'name' | 'status';
type StockColumn = 'date' | 'name' | 'received' | 'remaining' | 'total' | 'unit';
type SortDirection = 'ascending' | 'descending';
type ListSort<Column extends string> = { column: Column; direction: SortDirection };

const statusLabels = ['待進貨', '可使用', '已用完'] as const;
const names = new Intl.Collator('zh-TW', { numeric: true });

@Component({
  selector: 'app-material-inventory-page',
  imports: [DecimalPipe, FormsModule],
  templateUrl: './material-inventory-page.html',
  styleUrl: './material-inventory-page.css',
})
export class MaterialInventoryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(MaterialsService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly pesticide = this.route.snapshot.data['pesticide'] === true;
  protected readonly materialType = this.pesticide ? '農藥' : '肥料';
  protected readonly units = Object.values(MaterialPackageUnit);
  protected readonly unitLabels = pesticideUnits;
  protected readonly stockUnitOptions = this.units.map(unit => ({
    value: this.pesticide ? unit : this.unitLabels[unit],
    label: this.unitLabels[unit],
  }));
  protected readonly packageLabel = materialPackageLabel;
  protected readonly specs = signal<Specification[]>([]);
  protected readonly view = signal<'specs' | 'stock'>('specs');
  protected readonly filter = signal('');
  protected readonly specSort = signal<ListSort<SpecColumn>>({ column: 'name', direction: 'ascending' });
  protected readonly stockSort = signal<ListSort<StockColumn>>({ column: 'date', direction: 'descending' });
  protected readonly stockColumns: ReadonlyArray<{ key: StockColumn; label: string }> = [
    { key: 'date', label: '進貨日期' },
    { key: 'name', label: '名稱／規格' },
    { key: 'received', label: '進貨量' },
    { key: 'remaining', label: '剩餘量' },
    { key: 'total', label: '進貨總價' },
    { key: 'unit', label: '庫存單位單價' },
  ];
  protected readonly editor = signal<Editor | null>(null);
  protected readonly deletion = signal<Deletion | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly loadError = signal(false);
  protected readonly message = signal('');
  protected readonly feedback = signal('');

  protected today = taipeiToday();
  protected name = '';
  protected stockUnit = '';
  protected packageQuantity = '';
  protected packageUnit: MaterialPackageUnit | '' = '';
  protected materialId = '';
  protected receivedQuantity = '';
  protected purchasedOn = '';
  protected availableFrom = '';
  protected price = '';

  protected readonly matchingSpecs = computed(() => {
    const filter = this.filter().trim().toLocaleLowerCase('zh-TW');
    return this.specs().filter(spec => spec.name.toLocaleLowerCase('zh-TW').includes(filter));
  });
  protected readonly visibleSpecs = computed(() => {
    const { column, direction } = this.specSort();
    return [...this.matchingSpecs()].sort((a, b) => {
      const byName = compareSpecs(a, b);
      const order = column === 'name' ? byName : statusIndex(a) - statusIndex(b);
      return (direction === 'ascending' ? order : -order) || byName;
    });
  });
  protected readonly stockRows = computed(() => {
    const { column, direction } = this.stockSort();
    const rows = this.matchingSpecs().flatMap(spec => spec.lots.map(lot => ({
      spec, lot,
      date: lot.purchasedOn ?? lot.availableFrom,
      received: lot.receivedQuantity,
      remaining: lot.remainingQuantity,
      total: 'totalCost' in lot ? lot.totalCost :
        lot.unitPrice === null ? null : lot.receivedQuantity * lot.unitPrice,
      unit: lot.unitPrice,
    })));
    return rows.sort((a, b) => {
      let order: number;
      if (column === 'name') {
        order = compareSpecs(a.spec, b.spec);
      } else if (column === 'date') {
        order = a.date.localeCompare(b.date);
      } else {
        const first = a[column];
        const second = b[column];
        if (first === null && second !== null) return 1;
        if (second === null && first !== null) return -1;
        order = (first ?? 0) - (second ?? 0);
      }
      return (direction === 'ascending' ? order : -order) ||
        b.date.localeCompare(a.date) || compareSpecs(a.spec, b.spec);
    });
  });

  constructor() {
    void this.load();
  }

  protected status(spec: Specification) {
    return statusLabels[statusIndex(spec)];
  }

  protected sortBy<Column extends string>(
    sort: WritableSignal<ListSort<Column>>,
    column: NoInfer<Column>,
    initial: SortDirection = 'ascending',
  ): void {
    const previous = sort();
    const direction = previous.column === column
      ? previous.direction === 'ascending' ? 'descending' : 'ascending' : initial;
    sort.set({ column, direction });
  }

  protected sortIndicator(sort: ListSort<string>, column: string): string {
    if (sort.column !== column) return '↕';
    return sort.direction === 'ascending' ? '↑' : '↓';
  }

  protected stockUnitLabel(spec: Specification): string {
    return this.pesticide ? this.unitLabels[spec.stockUnit as PesticideStockUnit] : spec.stockUnit;
  }

  protected selectView(view: 'specs' | 'stock'): void {
    this.view.set(view);
    this.closeEditor();
    this.feedback.set('');
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(false);
    try {
      const materials = await this.read<FertilizerMaterialRsDto[]>(this.pesticide
        ? this.api.listPesticideMaterials() : this.api.listFertilizerMaterials());
      const specs = await this.read(from(materials).pipe(
        mergeMap(spec => {
          const parameters = { materialId: spec.id };
          const request: Observable<StockLot[]> = this.pesticide
            ? this.api.listPesticideLots(parameters) : this.api.listFertilizerLots(parameters);
          return request.pipe(map(lots => ({ ...spec, lots })));
        }, 6),
        toArray(),
      ));
      const byId = new Map(specs.map(spec => [spec.id, spec]));
      this.specs.set(materials.map(material => byId.get(material.id)!));
    } catch {
      this.loadError.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected openSpec(): void {
    this.name = '';
    this.stockUnit = '';
    this.packageQuantity = '';
    this.packageUnit = '';
    this.open({ kind: 'spec' });
  }

  protected openPurchase(spec?: Specification): void {
    this.today = taipeiToday();
    this.materialId = spec?.id ?? '';
    this.purchasedOn = this.today;
    this.receivedQuantity = '';
    this.price = '';
    this.open({ kind: 'purchase' });
  }

  protected openLot(spec: Specification, lot: StockLot): void {
    this.today = taipeiToday();
    this.materialId = spec.id;
    this.purchasedOn = lot.purchasedOn ?? '';
    this.availableFrom = lot.availableFrom;
    this.receivedQuantity = String(lot.receivedQuantity);
    this.price = lot.unitPrice === null ? '' : String(lot.unitPrice);
    this.open({ kind: 'lot', spec, lot });
  }

  protected selectedSpec(): Specification | undefined {
    return this.specs().find(spec => spec.id === this.materialId);
  }

  protected previewUnitPrice(): number | null {
    const quantity = parseOperationDecimal(this.receivedQuantity);
    const total = parsePurchaseTotal(this.price);
    if (!quantity || total === null) return null;
    return roundUnitPrice(total, this.receivedQuantity);
  }

  protected closeEditor(): void {
    this.editor.set(null);
    this.deletion.set(null);
    this.message.set('');
  }

  protected async save(form: Editor): Promise<void> {
    if (this.busy()) return;
    if (form.kind === 'spec') {
      await this.saveSpec();
    } else {
      await this.saveLot(form);
    }
  }

  protected confirmDelete(spec: Specification, lot?: StockLot): void {
    this.message.set('');
    this.feedback.set('');
    this.deletion.set({ spec, lot });
  }

  protected async remove({ spec, lot }: Deletion): Promise<void> {
    await this.mutate(
      () => {
        if (!lot) {
          const request = { materialId: spec.id };
          return this.pesticide
            ? this.api.deletePesticideMaterial(request)
            : this.api.deleteFertilizerMaterial(request);
        }
        return this.pesticide
          ? this.api.deletePesticideLot({ lotId: lot.id })
          : this.api.deleteFertilizerLot({ lotId: lot.id });
      },
      lot ? '已刪除批次。' : '已刪除規格。',
      '無法刪除庫存資料，請再試一次。',
    );
  }

  private open(form: Editor): void {
    this.message.set('');
    this.feedback.set('');
    this.editor.set(form);
  }

  private async saveSpec(): Promise<void> {
    const name = this.name.trim();
    const stockUnit = this.stockUnit.trim();
    if (!name || Array.from(name).length > 100 || !stockUnit || Array.from(stockUnit).length > 20) {
      this.message.set('請填寫資材名稱及庫存單位。名稱最多 100 字，單位最多 20 字。');
      return;
    }
    const quantity = parseOperationDecimal(this.packageQuantity);
    if (!quantity || !this.packageUnit) {
      this.message.set('請填寫大於零且小於 10 億的包裝數值，最多六位小數，並選擇包裝單位。');
      return;
    }

    const request = { name, stockUnit, package: { quantity, unit: this.packageUnit } };
    await this.mutate(() => {
      if (this.pesticide) {
        return this.api.createPesticideMaterial({
          createPesticideMaterialRqDto: { ...request, stockUnit: stockUnit as PesticideStockUnit },
        });
      }
      return this.api.createFertilizerMaterial({ createFertilizerMaterialRqDto: request });
    }, '已建立規格。', '無法儲存規格，請再試一次。', () => {
      this.filter.set('');
    });
  }

  private async saveLot(form: Exclude<Editor, { kind: 'spec' }>): Promise<void> {
    const material = this.selectedSpec();
    const receivedQuantity = parseOperationDecimal(this.receivedQuantity);
    const source = form.kind === 'lot' ? form.lot.source : 'purchase';
    const price = form.kind === 'purchase' ? parsePurchaseTotal(this.price) : parseOperationDecimal(this.price);
    if (!material || !receivedQuantity) {
      this.message.set('請選擇資材規格，並填寫大於零且小於 10 億的數量，最多六位小數。');
      return;
    }
    if ((source === 'purchase' && !this.purchasedOn) || this.purchasedOn > this.today ||
        (source === 'openingStock' && (!this.availableFrom || this.availableFrom > this.today))) {
      this.message.set('請填寫有效日期，購買日及盤點日皆不可晚於今天。');
      return;
    }
    if (price === null && (this.price.trim() || (this.pesticide && source === 'purchase'))) {
      this.message.set(form.kind === 'purchase'
        ? '請填寫有效的進貨總價。金額可為零，最多六位小數。'
        : '請填寫小於 10 億的非負單價，最多六位小數。');
      return;
    }
    const unitPrice = typeof price === 'bigint'
      ? roundUnitPrice(price, this.receivedQuantity) : price;
    if (unitPrice !== null && (unitPrice >= 1e9 || (unitPrice === 0 && typeof price === 'bigint' && price > 0n))) {
      this.message.set('換算後的單價超出可儲存範圍，請檢查進貨量與總價。');
      return;
    }
    const request = {
      purchasedOn: this.purchasedOn || null,
      availableFrom: source === 'openingStock' ? this.availableFrom : undefined,
      receivedQuantity,
      unitPrice,
    };
    await this.mutate(() => {
      if (form.kind === 'lot') {
        const editRequest = { lotId: form.lot.id, editFertilizerLotRqDto: request };
        return this.pesticide ? this.api.editPesticideLot(editRequest) : this.api.editFertilizerLot(editRequest);
      }
      return this.pesticide
        ? this.api.createPesticideLot({ materialId: material.id, createPesticideLotRqDto: { ...request, source } })
        : this.api.createFertilizerLot({ materialId: material.id, createFertilizerLotRqDto: { ...request, source } });
    }, form.kind === 'lot' ? '已更新批次。' : '已登記進貨。', '無法儲存庫存資料，請再試一次。', () => {
      this.filter.set('');
      this.view.set('stock');
    });
  }

  private async mutate(
    request: () => Observable<unknown>,
    success: string,
    fallback: string,
    afterSave?: () => void,
  ): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    try {
      await this.read(request());
      this.closeEditor();
      afterSave?.();
      this.feedback.set(success);
      await this.load();
    } catch (error) {
      this.message.set(operationError(error, fallback));
    } finally {
      this.busy.set(false);
    }
  }

  private read<T>(request: Observable<T>): Promise<T> {
    return firstValueFrom(request.pipe(takeUntilDestroyed(this.destroyRef)));
  }
}

function statusIndex(spec: Specification): number {
  if (!spec.lots.length) return 0;
  return spec.remainingQuantity > 0 ? 1 : 2;
}

function compareSpecs(a: Specification, b: Specification): number {
  const byName = names.compare(a.name, b.name);
  if (byName || !a.package || !b.package) return byName || Number(!!b.package) - Number(!!a.package);
  return a.package.quantity - b.package.quantity || names.compare(a.package.unit, b.package.unit);
}

function parsePurchaseTotal(input: string): bigint | null {
  const value = input.trim();
  // A quantity and unit price below one billion can produce a total of up to 18 digits.
  return /^(?:0*[0-9]{1,18}(?:\.[0-9]{0,6})?|\.[0-9]{1,6})$/.test(value) ? toMillionths(value) : null;
}

function roundUnitPrice(total: bigint, quantity: string): number {
  const scale = 1_000_000n;
  const scaledQuantity = toMillionths(quantity);
  // Round at six decimal places using the entered digits instead of floating-point division.
  const rounded = (total * scale * 2n + scaledQuantity) / (2n * scaledQuantity);
  return Number(rounded) / Number(scale);
}

function toMillionths(value: string): bigint {
  const [integer, fraction = ''] = value.trim().split('.');
  return BigInt(integer + fraction.padEnd(6, '0'));
}
