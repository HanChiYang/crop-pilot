import { PesticideStockUnit } from './generated/model/pesticideStockUnit';
import { MaterialPackageDto } from './generated/model/materialPackageDto';

export const pesticideUnits: Record<PesticideStockUnit, string> = {
  liter: '公升',
  milliliter: '毫升',
  kilogram: '公斤',
  gram: '公克',
};

export function materialPackageLabel(spec: MaterialPackageDto): string {
  return `${spec.quantity.toLocaleString('zh-TW', { maximumFractionDigits: 6 })} ${pesticideUnits[spec.unit]}`;
}
