import { DecimalPipe } from "@angular/common";
import { HttpErrorResponse } from "@angular/common/http";
import { Component, DestroyRef, computed, inject, signal } from "@angular/core";
import { takeUntilDestroyed, toSignal } from "@angular/core/rxjs-interop";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import {
  Subject,
  catchError,
  finalize,
  map,
  of,
  startWith,
  switchMap,
} from "rxjs";
import { OverviewService } from "./generated/api/overview.service";
import { PlantingsService } from "./generated/api/plantings.service";
import { FieldOverviewRsDto } from "./generated/model/fieldOverviewRsDto";
import { PlantingRsDto } from "./generated/model/plantingRsDto";
import { plantingStatusLabel } from "./planting-display";

type Row = PlantingRsDto & { fieldName: string };
type SortColumn =
  | "fieldName"
  | "cropName"
  | "areaSquareMeters"
  | "plannedPlantingDate"
  | "actualPlantingDate"
  | "status";
type Direction = "ascending" | "descending";
type ListState =
  | { status: "loading" | "error" }
  | { status: "ready"; fields: FieldOverviewRsDto[] };

const nameOrder = new Intl.Collator("zh-Hant-TW", { numeric: true });
const statusOrder = { planned: 0, growing: 1, harvesting: 2, completed: 3 };
const defaultSort = {
  column: "actualPlantingDate",
  direction: "descending",
} as const;

@Component({
  selector: "app-planting-list-page",
  imports: [DecimalPipe, RouterLink],
  templateUrl: "./planting-list-page.html",
  styleUrl: "./planting-list-page.css",
})
export class PlantingListPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly overview = inject(OverviewService);
  private readonly plantings = inject(PlantingsService);
  private readonly reload = new Subject<void>();
  private readonly saveInfo = this.router.currentNavigation()?.extras.info as
    { edited: boolean } | undefined;

  protected readonly state = toSignal(
    this.reload.pipe(
      startWith(undefined),
      switchMap(() =>
        this.overview.getOverview().pipe(
          map((fields): ListState => ({ status: "ready", fields })),
          startWith({ status: "loading" } as const),
          catchError((error: unknown) => {
            this.handleExpiredSession(error);
            return of({ status: "error" } as const);
          }),
        ),
      ),
    ),
    { requireSync: true },
  );
  protected readonly notice = signal(
    this.saveInfo
      ? this.saveInfo.edited
        ? "已更新栽培紀錄"
        : "已新增栽培紀錄"
      : "",
  );
  private readonly fieldId = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get("fieldId") ?? "")),
    { initialValue: this.route.snapshot.queryParamMap.get("fieldId") ?? "" },
  );
  protected readonly cropName = signal("");
  protected readonly search = signal("");
  protected readonly startDate = signal("");
  protected readonly endDate = signal("");
  protected readonly invalidDates = computed(
    () =>
      !!this.startDate() &&
      !!this.endDate() &&
      this.startDate() > this.endDate(),
  );
  protected readonly sort = signal<{
    column: SortColumn;
    direction: Direction;
  }>(defaultSort);
  protected readonly columns: ReadonlyArray<{
    key: SortColumn;
    label: string;
  }> = [
    { key: "fieldName", label: "田區" },
    { key: "cropName", label: "作物" },
    { key: "areaSquareMeters", label: "栽培面積" },
    { key: "plannedPlantingDate", label: "預定栽種日" },
    { key: "actualPlantingDate", label: "實際栽種日" },
    { key: "status", label: "狀態" },
  ];
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly rows = computed<Row[]>(() => {
    const state = this.state();
    return state.status === "ready"
      ? state.fields.flatMap((field) =>
          field.plantings.map((planting) => ({
            ...planting,
            fieldName: field.name,
          })),
        )
      : [];
  });
  protected readonly selectedFieldId = computed(() => {
    const state = this.state();
    return state.status === "ready" &&
      state.fields.some((field) => field.id === this.fieldId())
      ? this.fieldId()
      : "";
  });
  protected readonly cropNames = computed(() =>
    [
      ...new Set(
        this.rows()
          .filter(
            (row) =>
              !this.selectedFieldId() || row.fieldId === this.selectedFieldId(),
          )
          .map((row) => row.cropName),
      ),
    ].sort(nameOrder.compare),
  );
  protected readonly visibleRows = computed(() => {
    const term = searchKey(this.search().trim());
    const fieldId = this.selectedFieldId();
    const crop = this.cropName();
    const start = this.startDate();
    const end = this.endDate();
    const { column, direction } = this.sort();
    if (this.invalidDates()) return [];

    const inRange = (date: string | null) =>
      !!date && (!start || date >= start) && (!end || date <= end);

    return this.rows()
      .filter(
        (row) =>
          (!fieldId || row.fieldId === fieldId) &&
          (!crop || row.cropName === crop) &&
          (!term ||
            searchKey(row.fieldName).includes(term) ||
            searchKey(row.cropName).includes(term)) &&
          ((!start && !end) ||
            inRange(row.plannedPlantingDate) ||
            inRange(row.actualPlantingDate)),
      )
      .sort((a, b) => {
        const left = a[column];
        const right = b[column];
        if (left == null || right == null)
          return Number(left == null) - Number(right == null);
        const order =
          column === "status"
            ? statusOrder[a.status] - statusOrder[b.status]
            : typeof left === "number" && typeof right === "number"
              ? left - right
              : nameOrder.compare(String(left), String(right));
        return direction === "ascending" ? order : -order;
      });
  });

  protected readonly pendingDelete = signal<Row | null>(null);
  protected readonly deleteError = signal("");
  protected readonly deleteConflict = signal(false);
  protected readonly deleting = signal(false);

  protected reloadPlantings(): void {
    this.reload.next();
  }

  protected selectField(id: string): void {
    if (
      id &&
      !this.rows().some(
        (row) => row.fieldId === id && row.cropName === this.cropName(),
      )
    ) {
      this.cropName.set("");
    }
    // Keep the selected field when reloading or following an existing return link.
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { fieldId: id || null },
      replaceUrl: true,
    });
  }

  protected clearFilters(): void {
    this.search.set("");
    this.cropName.set("");
    this.startDate.set("");
    this.endDate.set("");
    this.selectField("");
  }

  protected sortBy(column: SortColumn): void {
    this.sort.update((current) => ({
      column,
      direction:
        current.column === column && current.direction === "ascending"
          ? "descending"
          : "ascending",
    }));
  }

  protected confirmDelete(
    row: Row,
    menu: HTMLElement,
    dialog: HTMLDialogElement,
  ): void {
    menu.hidePopover();
    this.pendingDelete.set(row);
    this.deleteError.set("");
    this.deleteConflict.set(false);
    dialog.showModal();
  }

  protected deletePlanting(dialog: HTMLDialogElement): void {
    const planting = this.pendingDelete();
    if (!planting || this.deleting() || this.deleteConflict()) return;
    this.deleting.set(true);
    this.deleteError.set("");
    this.plantings
      .deletePlanting({ plantingId: planting.id })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.deleting.set(false)),
      )
      .subscribe({
        next: () => {
          dialog.close();
          if (
            this.cropName() === planting.cropName &&
            !this.rows().some(
              (row) =>
                row.id !== planting.id &&
                row.cropName === planting.cropName &&
                (!this.selectedFieldId() ||
                  row.fieldId === this.selectedFieldId()),
            )
          ) {
            this.cropName.set("");
          }
          this.notice.set("已刪除栽培紀錄");
          this.reloadPlantings();
        },
        error: (error: unknown) => {
          this.handleExpiredSession(error);
          if (
            error instanceof HttpErrorResponse &&
            (error.status === 404 || error.status === 409)
          ) {
            this.deleteConflict.set(true);
            this.deleteError.set(
              error.status === 409
                ? "只有規劃中的栽培紀錄可以刪除。"
                : "找不到可刪除的栽培紀錄。",
            );
            this.reloadPlantings();
          } else {
            this.deleteError.set("目前無法刪除栽培紀錄，請再試一次。");
          }
        },
      });
  }

  private handleExpiredSession(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl("/login", { replaceUrl: true });
    }
  }
}

function searchKey(value: string): string {
  return value.normalize("NFKC").toLocaleLowerCase("zh-Hant-TW");
}
