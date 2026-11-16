import { ButtonOperationComponent } from './button-operation.component';
import { OperationRenderer } from './operation-renderers';
import { TextOperationComponent } from './text-operation.component';
import { EndorsementOperationComponent } from './underwriting/endorsement-operation.component';
import { QuoteOperationComponent } from './underwriting/quote-operation.component';

// The renderers for the sample models' custom operations. Adding an Operation(...) to a definition makes it
// appear in the schema; add a line here (and a component, if the payload needs one) to make it usable in the UI.
export const SAMPLE_OPERATION_RENDERERS: OperationRenderer[] = [
  // Operations without a payload, in any model that declares them (underwriting, the large forms, deep).
  { operation: 'SUBMIT', component: ButtonOperationComponent },
  { operation: 'BIND', component: ButtonOperationComponent },
  { operation: 'APPROVE', component: ButtonOperationComponent },

  // Underwriting operations with a payload.
  { operation: 'QUOTE', model: 'underwriting', component: QuoteOperationComponent },
  { operation: 'DECLINE', model: 'underwriting', component: TextOperationComponent, options: { placeholder: 'reason' } },
  { operation: 'ADD_ENDORSEMENT', model: 'underwriting', component: EndorsementOperationComponent },
];
