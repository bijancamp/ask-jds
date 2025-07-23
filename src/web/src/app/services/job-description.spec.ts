import { TestBed } from '@angular/core/testing';

import { JobDescription } from './job-description';

describe('JobDescription', () => {
  let service: JobDescription;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(JobDescription);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
